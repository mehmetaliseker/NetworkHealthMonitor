using NetworkHealthMonitor.Data;
using NetworkHealthMonitor.Models;

namespace NetworkHealthMonitor.Services;

public sealed class SchedulerService : ISchedulerService
{
    private static readonly TimeSpan PollInterval = TimeSpan.FromSeconds(AppSettings.SchedulerPollIntervalSeconds);

    private readonly DeviceRepository _deviceRepository;
    private readonly DeviceGroupRepository _deviceGroupRepository;
    private readonly SchedulePlanRepository _schedulePlanRepository;
    private readonly IPingExecutionService _pingExecutionService;
    private readonly SchedulePlanTargetResolver _targetResolver;
    private readonly IDeviceCheckPolicyService _deviceCheckPolicyService;
    private readonly AppSettingsService _settingsService;
    private readonly SemaphoreSlim _lifecycleLock = new(1, 1);
    private CancellationTokenSource? _cancellationTokenSource;
    private Task? _loopTask;

    public SchedulerService(
        DeviceRepository deviceRepository,
        DeviceGroupRepository deviceGroupRepository,
        SchedulePlanRepository schedulePlanRepository,
        IPingExecutionService pingExecutionService,
        SchedulePlanTargetResolver targetResolver,
        IDeviceCheckPolicyService deviceCheckPolicyService,
        AppSettingsService settingsService)
    {
        _deviceRepository = deviceRepository;
        _deviceGroupRepository = deviceGroupRepository;
        _schedulePlanRepository = schedulePlanRepository;
        _pingExecutionService = pingExecutionService;
        _targetResolver = targetResolver;
        _deviceCheckPolicyService = deviceCheckPolicyService;
        _settingsService = settingsService;
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

    private async Task RunLoopAsync(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                await RunDuePlansAsync(cancellationToken);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                Notify($"Otomatik kontrol sırasında hata oluştu: {ex.Message}");
            }

            await Task.Delay(PollInterval, cancellationToken);
        }
    }

    private async Task RunDuePlansAsync(CancellationToken cancellationToken)
    {
        var plans = await _schedulePlanRepository.GetActiveAsync();
        if (plans.Count == 0)
        {
            return;
        }

        var devices = await _deviceRepository.GetAutoCheckCandidatesAsync();
        var groups = await _deviceGroupRepository.GetAllAsync();
        var settings = await _settingsService.LoadAsync();
        var now = DateTime.Now;
        var groupsById = groups.ToDictionary(group => group.Id);
        var groupsByName = groups.ToDictionary(group => group.Name, StringComparer.OrdinalIgnoreCase);
        var dispatchedDeviceIds = new HashSet<int>();

        foreach (var plan in plans)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var targets = _targetResolver.ResolveTargets(plan, devices, groups, respectAutoCheck: true);
            if (targets.Count == 0)
            {
                Notify($"{plan.Name}: uygun hedef cihaz bulunamadı.");
                continue;
            }

            var dueTargets = targets
                .Where(device =>
                {
                    if (dispatchedDeviceIds.Contains(device.Id))
                    {
                        return false;
                    }

                    var group = ResolveGroup(device, groupsById, groupsByName);
                    var policy = _deviceCheckPolicyService.ResolvePolicy(device, group, plan, settings, plan.ToPingOptions());
                    return _deviceCheckPolicyService.IsDue(device, policy, now);
                })
                .ToList();

            if (dueTargets.Count == 0)
            {
                continue;
            }

            Notify($"{plan.Name}: {dueTargets.Count} cihaz kontrol ediliyor.");
            foreach (var device in dueTargets)
            {
                dispatchedDeviceIds.Add(device.Id);
            }

            var result = await _pingExecutionService.PingDevicesAsync(
                dueTargets,
                plan.ToPingOptions(),
                PingTriggerType.Scheduled,
                plan,
                progress: null,
                cancellationToken);

            await _schedulePlanRepository.UpdateLastRunAsync(plan.Id, DateTime.Now);
            Notify($"{plan.Name}: {result.SuccessCount} başarılı, {result.FailureCount} başarısız, {result.SkippedBecauseAlreadyRunning} çakışma atlandı.", shouldRefresh: true);
        }
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

    private void Notify(string message, bool shouldRefresh = false)
    {
        StatusChanged?.Invoke(this, new SchedulerStatusChangedEventArgs(message, shouldRefresh));
    }
}
