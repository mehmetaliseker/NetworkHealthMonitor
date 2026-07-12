using NetworkHealthMonitor.Data;
using NetworkHealthMonitor.Models;

namespace NetworkHealthMonitor.Services;

public sealed class SchedulerService : ISchedulerService
{
    private readonly DeviceRepository _deviceRepository;
    private readonly DeviceGroupRepository _deviceGroupRepository;
    private readonly SchedulePlanRepository _schedulePlanRepository;
    private readonly PingLogRepository _pingLogRepository;
    private readonly IPingExecutionService _pingExecutionService;
    private readonly SchedulePlanTargetResolver _targetResolver;
    private readonly AppSettingsService _settingsService;
    private readonly ScheduleTimingService _timingService;
    private readonly ISystemClock _clock;
    private readonly SchedulerRuntimeOptions _runtimeOptions;
    private readonly SemaphoreSlim _lifecycleLock = new(1, 1);
    private CancellationTokenSource? _cancellationTokenSource;
    private Task? _loopTask;

    public SchedulerService(
        DeviceRepository deviceRepository,
        DeviceGroupRepository deviceGroupRepository,
        SchedulePlanRepository schedulePlanRepository,
        PingLogRepository pingLogRepository,
        IPingExecutionService pingExecutionService,
        SchedulePlanTargetResolver targetResolver,
        IDeviceCheckPolicyService deviceCheckPolicyService,
        AppSettingsService settingsService,
        ScheduleTimingService? timingService = null,
        ISystemClock? clock = null,
        SchedulerRuntimeOptions? runtimeOptions = null)
    {
        _deviceRepository = deviceRepository;
        _deviceGroupRepository = deviceGroupRepository;
        _schedulePlanRepository = schedulePlanRepository;
        _pingLogRepository = pingLogRepository;
        _pingExecutionService = pingExecutionService;
        _targetResolver = targetResolver;
        _settingsService = settingsService;
        _timingService = timingService ?? new ScheduleTimingService();
        _clock = clock ?? new SystemClock();
        _runtimeOptions = runtimeOptions ?? new SchedulerRuntimeOptions();
    }

    public event EventHandler<SchedulerStatusChangedEventArgs>? StatusChanged;

    public bool IsRunning => _loopTask is { IsCompleted: false };

    public async Task StartAsync(CancellationToken cancellationToken = default)
    {
        await _lifecycleLock.WaitAsync(cancellationToken);
        try
        {
            if (IsRunning)
            {
                return;
            }

            _cancellationTokenSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            _loopTask = Task.Run(() => RunLoopAsync(_cancellationTokenSource.Token), CancellationToken.None);
            Notify("Otomatik kontrol planları başlatıldı.");
        }
        finally
        {
            _lifecycleLock.Release();
        }
    }

    public async Task StopAsync()
    {
        await _lifecycleLock.WaitAsync();
        try
        {
            if (_cancellationTokenSource is null)
            {
                return;
            }

            _cancellationTokenSource.Cancel();
            if (_loopTask is not null)
            {
                try
                {
                    await _loopTask;
                }
                catch (OperationCanceledException)
                {
                    // Expected during shutdown.
                }
            }

            _cancellationTokenSource.Dispose();
            _cancellationTokenSource = null;
            _loopTask = null;
            Notify("Otomatik kontrol planları durduruldu.");
        }
        finally
        {
            _lifecycleLock.Release();
        }
    }

    public async ValueTask DisposeAsync()
    {
        await StopAsync();
        _lifecycleLock.Dispose();
    }

    public async Task RunDuePlansOnceAsync(CancellationToken cancellationToken = default)
    {
        var settings = await _settingsService.LoadAsync();
        if (!settings.AutoCheckEnabled)
        {
            return;
        }

        var plans = await _schedulePlanRepository.GetActiveAsync();
        if (plans.Count == 0)
        {
            return;
        }

        var devices = await _deviceRepository.GetAutoCheckCandidatesAsync();
        var groups = await _deviceGroupRepository.GetAllAsync();
        var now = _clock.Now;
        var cycleLogsByDeviceId = new Dictionary<int, PingLog>();

        foreach (var plan in plans)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!_timingService.IsDue(plan, now))
            {
                continue;
            }

            try
            {
                await RunDuePlanAsync(plan, devices, groups, cycleLogsByDeviceId, now, cancellationToken);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                var nextRunAt = _timingService.CalculateNextRunAfterExecution(plan, _clock.Now);
                await _schedulePlanRepository.UpdateRunStateAsync(plan.Id, _clock.Now, nextRunAt, $"Hata: {ex.Message}");
                Notify($"{plan.Name}: otomatik kontrol sırasında hata oluştu: {ex.Message}", shouldRefresh: true);
            }
        }
    }

    private async Task RunLoopAsync(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                await RunDuePlansOnceAsync(cancellationToken);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                Notify($"Otomatik kontrol sırasında hata oluştu: {ex.Message}");
            }

            await Task.Delay(await ResolvePollIntervalAsync(), cancellationToken);
        }
    }

    private async Task RunDuePlanAsync(
        SchedulePlan plan,
        IReadOnlyList<Device> devices,
        IReadOnlyList<DeviceGroup> groups,
        Dictionary<int, PingLog> cycleLogsByDeviceId,
        DateTime dueTime,
        CancellationToken cancellationToken)
    {
        var targets = _targetResolver.ResolveTargets(plan, devices, groups, respectAutoCheck: true);
        if (targets.Count == 0)
        {
            await CompletePlanWithoutPingAsync(plan, dueTime, "Uygun hedef cihaz bulunamadı.");
            Notify($"{plan.Name}: uygun hedef cihaz bulunamadı.", shouldRefresh: true);
            return;
        }

        var associatedLogs = targets
            .Where(device => cycleLogsByDeviceId.ContainsKey(device.Id))
            .Select(device => CreateAssociatedPlanLog(cycleLogsByDeviceId[device.Id], plan))
            .ToList();

        if (associatedLogs.Count > 0)
        {
            await _pingLogRepository.AddRangeAsync(associatedLogs);
        }

        var dueTargets = targets
            .Where(device => !cycleLogsByDeviceId.ContainsKey(device.Id))
            .ToList();

        if (dueTargets.Count == 0)
        {
            var associatedStatus = $"{associatedLogs.Count} cihaz sonucu mevcut çevrimden ilişkilendirildi.";
            await CompletePlanWithoutPingAsync(plan, dueTime, associatedStatus);
            Notify($"{plan.Name}: {associatedStatus}", shouldRefresh: true);
            return;
        }

        Notify($"{plan.Name}: {dueTargets.Count} cihaz kontrol ediliyor.");
        var result = await _pingExecutionService.PingDevicesAsync(
            dueTargets,
            plan.ToPingOptions(),
            PingTriggerType.Scheduled,
            plan,
            progress: null,
            cancellationToken);

        foreach (var log in result.Logs)
        {
            if (log.DeviceId.HasValue)
            {
                cycleLogsByDeviceId[log.DeviceId.Value] = log;
            }
        }

        var completedAt = _clock.Now;
        var status = $"{result.SuccessCount} başarılı, {result.FailureCount} başarısız, {result.SkippedBecauseAlreadyRunning} çakışma atlandı, {associatedLogs.Count} sonuç ilişkilendirildi.";
        await _schedulePlanRepository.UpdateRunStateAsync(
            plan.Id,
            completedAt,
            _timingService.CalculateNextRunAfterExecution(plan, completedAt),
            status);
        Notify($"{plan.Name}: {status}", shouldRefresh: true);
    }

    private static PingLog CreateAssociatedPlanLog(PingLog source, SchedulePlan plan)
    {
        return new PingLog
        {
            DeviceId = source.DeviceId,
            DeviceName = source.DeviceName,
            IpAddress = source.IpAddress,
            DeviceType = source.DeviceType,
            GroupName = source.GroupName,
            Status = source.Status,
            LatencyMs = source.LatencyMs,
            ResponseMessage = source.ResponseMessage,
            ErrorMessage = source.ErrorMessage,
            CheckedAt = source.CheckedAt,
            TriggerType = PingTriggerType.Scheduled,
            SchedulePlanId = plan.Id,
            SchedulePlanName = plan.Name
        };
    }

    private async Task CompletePlanWithoutPingAsync(SchedulePlan plan, DateTime dueTime, string status)
    {
        await _schedulePlanRepository.UpdateRunStateAsync(
            plan.Id,
            dueTime,
            _timingService.CalculateNextRunAfterExecution(plan, dueTime),
            status);
    }

    private async Task<TimeSpan> ResolvePollIntervalAsync()
    {
        if (_runtimeOptions.PollIntervalOverride.HasValue)
        {
            return _runtimeOptions.PollIntervalOverride.Value;
        }

        var settings = await _settingsService.LoadAsync();
        var pollIntervalSeconds = Math.Clamp(
            settings.SchedulerPollIntervalSeconds,
            AppSettings.MinSchedulerPollIntervalSeconds,
            AppSettings.MaxSchedulerPollIntervalSeconds);
        return TimeSpan.FromSeconds(pollIntervalSeconds);
    }

    private void Notify(string message, bool shouldRefresh = false)
    {
        StatusChanged?.Invoke(this, new SchedulerStatusChangedEventArgs(message, shouldRefresh));
    }
}
