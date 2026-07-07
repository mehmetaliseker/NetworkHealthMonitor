using NetworkHealthMonitor.Models;

namespace NetworkHealthMonitor.Services;

public interface IPingService
{
    Task<PingDeviceResult> PingAsync(Device device, PingOptions options, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<PingDeviceResult>> PingManyAsync(
        IEnumerable<Device> devices,
        PingOptions options,
        IProgress<PingProgress>? progress,
        CancellationToken cancellationToken = default);
}
