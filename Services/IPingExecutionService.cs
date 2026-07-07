using NetworkHealthMonitor.Models;

namespace NetworkHealthMonitor.Services;

public interface IPingExecutionService
{
    Task<PingExecutionResult> PingDevicesAsync(
        IEnumerable<Device> devices,
        PingOptions options,
        PingTriggerType triggerType,
        SchedulePlan? schedulePlan = null,
        IProgress<PingProgress>? progress = null,
        CancellationToken cancellationToken = default);
}
