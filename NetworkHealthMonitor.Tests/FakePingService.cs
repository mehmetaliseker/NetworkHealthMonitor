using NetworkHealthMonitor.Models;
using NetworkHealthMonitor.Services;

namespace NetworkHealthMonitor.Tests;

internal sealed class FakePingService : IPingService
{
    public int PingCount { get; private set; }

    public List<int> PingedDeviceIds { get; } = new();

    public Task<PingDeviceResult> PingAsync(Device device, PingOptions options, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        PingCount++;
        PingedDeviceIds.Add(device.Id);
        return Task.FromResult(new PingDeviceResult(device, true, 1, DateTime.Now, "Fake ping success", string.Empty));
    }

    public async Task<IReadOnlyList<PingDeviceResult>> PingManyAsync(
        IEnumerable<Device> devices,
        PingOptions options,
        IProgress<PingProgress>? progress,
        CancellationToken cancellationToken = default)
    {
        var targets = devices.ToList();
        var results = new List<PingDeviceResult>();
        var completed = 0;
        foreach (var device in targets)
        {
            var result = await PingAsync(device, options, cancellationToken);
            results.Add(result);
            completed++;
            progress?.Report(new PingProgress(targets.Count, completed, completed, 0, device.Id, DeviceStatus.Online, 1, result.CheckedAt));
        }

        return results;
    }
}
