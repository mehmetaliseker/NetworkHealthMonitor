using NetworkHealthMonitor.Data;
using NetworkHealthMonitor.Infrastructure;
using NetworkHealthMonitor.Models;
using System.Diagnostics;

namespace NetworkHealthMonitor.Services;

public sealed class SchedulerService : ISchedulerService
{
    private readonly DeviceRepository _deviceRepository;
    private readonly DeviceGroupRepository _deviceGroupRepository;
    private readonly SchedulePlanRepository _schedulePlanRepository;
    private readonly PingLogRepository _pingLogRepository;
    private readonly IPingExecutionService _pingExecutionService;
    private readonly SchedulePlanTargetResolver _targetResolver;
    private readonly IDeviceCheckPolicyService _deviceCheckPolicyService;
    private readonly AppSettingsService _settingsService;
    private readonly ScheduleTimingService _timingService;
    private readonly ISystemClock _clock;
    private readonly SchedulerRuntimeOptions _runtimeOptions;
    private readonly WorkerHeartbeatRepository? _heartbeatRepository;
    private readonly AvailabilityRepository? _availabilityRepository;
    private readonly IIncidentService? _incidentService;
    private readonly string? _workerInstanceId;
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
        SchedulerRuntimeOptions? runtimeOptions = null,
        WorkerHeartbeatRepository? heartbeatRepository = null,
        AvailabilityRepository? availabilityRepository = null,
        IIncidentService? incidentService = null,
        string? workerInstanceId = null)
    {
        _deviceRepository = deviceRepository;
        _deviceGroupRepository = deviceGroupRepository;
        _schedulePlanRepository = schedulePlanRepository;
        _pingLogRepository = pingLogRepository;
        _pingExecutionService = pingExecutionService;
        _targetResolver = targetResolver;
        _deviceCheckPolicyService = deviceCheckPolicyService;
        _settingsService = settingsService;
        _timingService = timingService ?? new ScheduleTimingService();
        _clock = clock ?? new SystemClock();
        _runtimeOptions = runtimeOptions ?? new SchedulerRuntimeOptions();
        _heartbeatRepository = heartbeatRepository;
        _availabilityRepository = availabilityRepository;
        _incidentService = incidentService;
        _workerInstanceId = workerInstanceId;
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
            Notify("Otomatik kontrol planlari baslatildi.");
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
            Notify("Otomatik kontrol planlari durduruldu.");
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
        var cycleWatch = Stopwatch.StartNew();
        var settings = await _settingsService.LoadAsync();
        var nowUtc = ToUtc(_clock.Now);
        await MarkSchedulerPollAsync(nowUtc, cancellationToken);
        await _deviceRepository.ExpireSuppressionsAsync(nowUtc, cancellationToken);
        await EvaluateOpenIncidentsAsync(cancellationToken);
        if (!settings.AutoCheckEnabled)
        {
            await MarkSchedulerCycleAsync(cycleWatch.Elapsed.TotalMilliseconds, cancellationToken);
            return;
        }

        var plans = await _schedulePlanRepository.GetActiveAsync();
        if (plans.Count == 0)
        {
            await MarkSchedulerCycleAsync(cycleWatch.Elapsed.TotalMilliseconds, cancellationToken);
            return;
        }

        var devices = await _deviceRepository.GetAutoCheckCandidatesAsync();
        if (_availabilityRepository is not null)
        {
            await _availabilityRepository.ReconcileMaintenanceWindowsAsync(nowUtc, cancellationToken);
            var defaultCheckIntervalSeconds = Math.Max(
                AppSettings.MinDeviceCheckIntervalSeconds,
                settings.AutoCheckIntervalMinutes * 60);
            await _availabilityRepository.ReconcileExpectedCheckGapsAsync(
                settings.ExpectedCheckGraceMultiplier,
                defaultCheckIntervalSeconds,
                nowUtc,
                cancellationToken);
        }

        var groups = await _deviceGroupRepository.GetAllAsync();
        var cycleLogsByDeviceId = new Dictionary<int, PingLog>();

        foreach (var plan in plans.OrderBy(GetPlanPriority).ThenBy(plan => plan.Id))
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!_timingService.IsDue(plan, nowUtc))
            {
                continue;
            }

            try
            {
                await RunDuePlanAsync(plan, devices, groups, cycleLogsByDeviceId, nowUtc, cancellationToken);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                var failedAt = ToUtc(_clock.Now);
                var nextRunAt = _timingService.CalculateNextRunAfterExecution(plan, failedAt);
                await _schedulePlanRepository.UpdateRunStateAsync(plan.Id, failedAt, nextRunAt, $"Hata: {ex.Message}");
                await MarkSchedulerExceptionAsync(ex, cancellationToken);
                Notify($"{plan.Name}: otomatik kontrol sirasinda hata olustu: {ex.Message}", shouldRefresh: true);
            }
        }

        await RunDueFailureRechecksAsync(plans, devices, groups, settings, cycleLogsByDeviceId, ToUtc(_clock.Now), cancellationToken);
        await MarkSchedulerCycleAsync(cycleWatch.Elapsed.TotalMilliseconds, cancellationToken);
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
                Notify($"Otomatik kontrol sirasinda hata olustu: {ex.Message}");
            }

            await Task.Delay(await ResolvePollIntervalAsync(), cancellationToken);
        }
    }

    private async Task RunDuePlanAsync(
        SchedulePlan plan,
        IReadOnlyList<Device> devices,
        IReadOnlyList<DeviceGroup> groups,
        Dictionary<int, PingLog> cycleLogsByDeviceId,
        DateTime dueTimeUtc,
        CancellationToken cancellationToken)
    {
        var targets = _targetResolver.ResolveTargets(plan, devices, groups, respectAutoCheck: true);
        if (targets.Count == 0)
        {
            await CompletePlanWithoutPingAsync(plan, dueTimeUtc, "Uygun hedef cihaz bulunamadi.");
            Notify($"{plan.Name}: uygun hedef cihaz bulunamadi.", shouldRefresh: true);
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
            var associatedStatus = $"{associatedLogs.Count} cihaz sonucu mevcut cevrimden iliskilendirildi.";
            await CompletePlanWithoutPingAsync(plan, dueTimeUtc, associatedStatus);
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

        var completedAt = ToUtc(_clock.Now);
        var status = $"{result.SuccessCount} basarili, {result.FailureCount} basarisiz, {result.SkippedBecauseAlreadyRunning} cakisma atlandi, {associatedLogs.Count} sonuc iliskilendirildi.";
        await _schedulePlanRepository.UpdateRunStateAsync(
            plan.Id,
            completedAt,
            _timingService.CalculateNextRunAfterExecution(plan, completedAt),
            status);
        Notify($"{plan.Name}: {status}", shouldRefresh: true);
    }

    private async Task RunDueFailureRechecksAsync(
        IReadOnlyList<SchedulePlan> plans,
        IReadOnlyList<Device> devices,
        IReadOnlyList<DeviceGroup> groups,
        AppSettings settings,
        Dictionary<int, PingLog> cycleLogsByDeviceId,
        DateTime nowUtc,
        CancellationToken cancellationToken)
    {
        var groupsById = groups.ToDictionary(group => group.Id);
        var groupsByName = groups.ToDictionary(group => group.Name, StringComparer.OrdinalIgnoreCase);
        var dueByPlan = new Dictionary<int, List<Device>>();

        foreach (var device in devices)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (cycleLogsByDeviceId.ContainsKey(device.Id)
                || !device.LastStatus.IsFailureObservation()
                || device.IsMonitoringPaused)
            {
                continue;
            }

            var effectivePlan = ResolveEffectivePlan(device, plans, groups, groupsById, groupsByName);
            if (effectivePlan is null)
            {
                continue;
            }

            var group = ResolveGroup(device, groupsById, groupsByName);
            var policy = _deviceCheckPolicyService.ResolvePolicy(device, group, effectivePlan, settings, effectivePlan.ToPingOptions());
            if (!_deviceCheckPolicyService.IsDue(device, policy, nowUtc))
            {
                continue;
            }

            if (!dueByPlan.TryGetValue(effectivePlan.Id, out var planDevices))
            {
                planDevices = new List<Device>();
                dueByPlan[effectivePlan.Id] = planDevices;
            }

            planDevices.Add(device);
        }

        foreach (var planGroup in dueByPlan)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var plan = plans.First(item => item.Id == planGroup.Key);
            var dueTargets = planGroup.Value
                .Where(device => !cycleLogsByDeviceId.ContainsKey(device.Id))
                .ToList();
            if (dueTargets.Count == 0)
            {
                continue;
            }

            Notify($"{plan.Name}: {dueTargets.Count} erisilemeyen cihaz yeniden kontrol ediliyor.");
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

            Notify($"{plan.Name}: erisilemeyen cihaz yeniden kontrolu {result.SuccessCount} basarili, {result.FailureCount} basarisiz, {result.SkippedBecauseAlreadyRunning} cakisma atlandi.", shouldRefresh: true);
        }
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
            IsReachable = source.IsReachable,
            LatencyMs = source.LatencyMs,
            ResponseMessage = source.ResponseMessage,
            ErrorCode = source.ErrorCode,
            ErrorMessage = source.ErrorMessage,
            CheckedAt = source.CheckedAt,
            Source = PingTriggerType.Scheduled.ToStorageValue(),
            TriggerType = PingTriggerType.Scheduled,
            PlanId = plan.Id,
            SchedulePlanId = plan.Id,
            SchedulePlanName = plan.Name,
            WorkerInstanceId = source.WorkerInstanceId
        };
    }

    private async Task CompletePlanWithoutPingAsync(SchedulePlan plan, DateTime dueTimeUtc, string status)
    {
        await _schedulePlanRepository.UpdateRunStateAsync(
            plan.Id,
            dueTimeUtc,
            _timingService.CalculateNextRunAfterExecution(plan, dueTimeUtc),
            status);
    }

    private static SchedulePlan? ResolveEffectivePlan(
        Device device,
        IReadOnlyList<SchedulePlan> plans,
        IReadOnlyList<DeviceGroup> groups,
        IReadOnlyDictionary<int, DeviceGroup> groupsById,
        IReadOnlyDictionary<string, DeviceGroup> groupsByName)
    {
        if (device.DefaultSchedulePlanId.HasValue)
        {
            var devicePlan = plans.FirstOrDefault(plan => plan.Id == device.DefaultSchedulePlanId.Value && plan.IsActive);
            if (devicePlan is not null)
            {
                return devicePlan;
            }
        }

        var group = ResolveGroup(device, groupsById, groupsByName);
        if (group?.DefaultSchedulePlanId is int groupPlanId)
        {
            var groupDefaultPlan = plans.FirstOrDefault(plan => plan.Id == groupPlanId && plan.IsActive);
            if (groupDefaultPlan is not null)
            {
                return groupDefaultPlan;
            }
        }

        return plans
            .Where(plan => PlanTargetsDevice(plan, device))
            .OrderBy(GetPlanPriority)
            .ThenBy(plan => plan.Id)
            .FirstOrDefault();
    }

    private static bool PlanTargetsDevice(SchedulePlan plan, Device device)
    {
        return plan.TargetType switch
        {
            SchedulePlanTargetType.Device => int.TryParse(plan.TargetValue, out var id)
                ? device.Id == id
                : string.Equals(device.IpAddress, plan.TargetValue, StringComparison.OrdinalIgnoreCase),
            SchedulePlanTargetType.DeviceType => device.DeviceType == DeviceTypeExtensions.FromStorageValue(plan.TargetValue),
            SchedulePlanTargetType.DeviceGroup => int.TryParse(plan.TargetValue, out var groupId)
                ? device.GroupId == groupId
                : string.Equals(device.GroupName, plan.TargetValue, StringComparison.OrdinalIgnoreCase),
            SchedulePlanTargetType.CriticalDevices => device.IsCritical,
            SchedulePlanTargetType.AllDevices => true,
            _ => false
        };
    }

    private static int GetPlanPriority(SchedulePlan plan)
    {
        return plan.TargetType switch
        {
            SchedulePlanTargetType.Device => 0,
            SchedulePlanTargetType.DeviceGroup => 1,
            SchedulePlanTargetType.DeviceType => 2,
            SchedulePlanTargetType.CriticalDevices => 3,
            SchedulePlanTargetType.AllDevices => 4,
            _ => 5
        };
    }

    private static DeviceGroup? ResolveGroup(
        Device device,
        IReadOnlyDictionary<int, DeviceGroup> groupsById,
        IReadOnlyDictionary<string, DeviceGroup> groupsByName)
    {
        if (device.GroupId.HasValue && groupsById.TryGetValue(device.GroupId.Value, out var groupById))
        {
            return groupById;
        }

        return !string.IsNullOrWhiteSpace(device.GroupName) && groupsByName.TryGetValue(device.GroupName, out var groupByName)
            ? groupByName
            : null;
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

    private async Task MarkSchedulerCycleAsync(double elapsedMilliseconds, CancellationToken cancellationToken)
    {
        if (_heartbeatRepository is null || string.IsNullOrWhiteSpace(_workerInstanceId))
        {
            return;
        }

        await _heartbeatRepository.MarkSchedulerCycleAsync(_workerInstanceId, DateTime.UtcNow, elapsedMilliseconds, cancellationToken);
    }

    private async Task MarkSchedulerPollAsync(DateTime whenUtc, CancellationToken cancellationToken)
    {
        if (_heartbeatRepository is null || string.IsNullOrWhiteSpace(_workerInstanceId))
        {
            return;
        }

        await _heartbeatRepository.MarkSchedulerPollAsync(_workerInstanceId, whenUtc, cancellationToken);
    }

    private async Task EvaluateOpenIncidentsAsync(CancellationToken cancellationToken)
    {
        if (_incidentService is null)
        {
            return;
        }

        try
        {
            await _incidentService.EvaluateOpenIncidentsAsync(cancellationToken);
        }
        catch (Exception ex)
        {
            AppErrorLogger.Log(ex, "Incident escalation evaluation failed.");
            await MarkSchedulerExceptionAsync(ex, cancellationToken);
        }
    }

    private async Task MarkSchedulerExceptionAsync(Exception exception, CancellationToken cancellationToken)
    {
        if (_heartbeatRepository is null || string.IsNullOrWhiteSpace(_workerInstanceId))
        {
            return;
        }

        try
        {
            await _heartbeatRepository.MarkDiagnosticErrorAsync(
                _workerInstanceId,
                exception is Microsoft.Data.Sqlite.SqliteException sqlite && sqlite.SqliteErrorCode == 5
                    ? "LastDatabaseLockedError"
                    : "LastSchedulerException",
                exception.Message,
                cancellationToken);
        }
        catch
        {
            // Diagnostics must not break scheduler execution.
        }
    }

    private static DateTime ToUtc(DateTime value)
    {
        return value.Kind switch
        {
            DateTimeKind.Utc => value,
            DateTimeKind.Local => value.ToUniversalTime(),
            _ => DateTime.SpecifyKind(value, DateTimeKind.Utc)
        };
    }
}
