using NetworkHealthMonitor.Data;
using NetworkHealthMonitor.Models;

namespace NetworkHealthMonitor.Services;

public sealed class SchedulerService : ISchedulerService
{
    private static readonly TimeSpan PollInterval = TimeSpan.FromSeconds(15);

    private readonly DeviceRepository _deviceRepository;
    private readonly SchedulePlanRepository _schedulePlanRepository;
    private readonly IPingExecutionService _pingExecutionService;
    private readonly SemaphoreSlim _lifecycleLock = new(1, 1);
    private CancellationTokenSource? _cancellationTokenSource;
    private Task? _loopTask;

    public SchedulerService(
        DeviceRepository deviceRepository,
        SchedulePlanRepository schedulePlanRepository,
        IPingExecutionService pingExecutionService)
    {
        _deviceRepository = deviceRepository;
        _schedulePlanRepository = schedulePlanRepository;
        _pingExecutionService = pingExecutionService;
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

        var devices = await _deviceRepository.GetAllAsync();
        var now = DateTime.Now;

        foreach (var plan in plans)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!IsDue(plan, now))
            {
                continue;
            }

            var targets = ResolveTargets(plan, devices).ToList();
            if (targets.Count == 0)
            {
                await _schedulePlanRepository.UpdateLastRunAsync(plan.Id, now);
                Notify($"{plan.Name}: uygun hedef cihaz bulunamadı.");
                continue;
            }

            Notify($"{plan.Name}: {targets.Count} cihaz kontrol ediliyor.");
            var result = await _pingExecutionService.PingDevicesAsync(
                targets,
                plan.ToPingOptions(),
                PingTriggerType.Scheduled,
                plan,
                progress: null,
                cancellationToken);

            await _schedulePlanRepository.UpdateLastRunAsync(plan.Id, DateTime.Now);
            Notify($"{plan.Name}: {result.SuccessCount} başarılı, {result.FailureCount} başarısız.");
        }
    }

    private static bool IsDue(SchedulePlan plan, DateTime now)
    {
        if (!plan.LastRunAt.HasValue)
        {
            return true;
        }

        return now - plan.LastRunAt.Value >= TimeSpan.FromMinutes(Math.Max(1, plan.IntervalMinutes));
    }

    private static IEnumerable<Device> ResolveTargets(SchedulePlan plan, IReadOnlyList<Device> devices)
    {
        var activeDevices = devices.Where(device => device.IsActive && device.AutoCheckEnabled);
        return plan.TargetType switch
        {
            SchedulePlanTargetType.Device => activeDevices.Where(device => MatchesDeviceTarget(device, plan.TargetValue)),
            SchedulePlanTargetType.DeviceType => activeDevices.Where(device => device.DeviceType == DeviceTypeExtensions.FromStorageValue(plan.TargetValue)),
            SchedulePlanTargetType.DeviceGroup => activeDevices.Where(device => MatchesGroupTarget(device, plan.TargetValue)),
            SchedulePlanTargetType.CriticalDevices => activeDevices.Where(device => device.IsCritical),
            SchedulePlanTargetType.AllDevices => activeDevices,
            _ => activeDevices
        };
    }

    private static bool MatchesDeviceTarget(Device device, string targetValue)
    {
        return int.TryParse(targetValue, out var id)
            ? device.Id == id
            : string.Equals(device.IpAddress, targetValue, StringComparison.OrdinalIgnoreCase);
    }

    private static bool MatchesGroupTarget(Device device, string targetValue)
    {
        return int.TryParse(targetValue, out var id)
            ? device.GroupId == id
            : string.Equals(device.GroupName, targetValue, StringComparison.OrdinalIgnoreCase);
    }

    private void Notify(string message)
    {
        StatusChanged?.Invoke(this, new SchedulerStatusChangedEventArgs(message));
    }
}
