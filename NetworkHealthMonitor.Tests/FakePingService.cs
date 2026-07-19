using NetworkHealthMonitor.Models;
using NetworkHealthMonitor.Services;

namespace NetworkHealthMonitor.Tests;

internal sealed class FakePingService : IPingService
{
    private readonly Queue<bool> _results = new();
    private readonly Func<DateTime> _nowProvider;

    public FakePingService(params bool[] results)
        : this(() => DateTime.Now, results)
    {
    }

    public FakePingService(Func<DateTime> nowProvider, params bool[] results)
    {
        _nowProvider = nowProvider;
        foreach (var result in results)
        {
            _results.Enqueue(result);
        }
    }

    public int PingCount { get; private set; }

    public List<int> PingedDeviceIds { get; } = new();

    public Task<PingDeviceResult> PingAsync(Device device, PingOptions options, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        PingCount++;
        PingedDeviceIds.Add(device.Id);
        var success = _results.Count == 0 || _results.Dequeue();
        var checkedAt = _nowProvider();
        return Task.FromResult(success
            ? new PingDeviceResult(device, true, 1, checkedAt, "Fake ping success", string.Empty)
            : new PingDeviceResult(device, false, null, checkedAt, "Fake ping failure", "timeout"));
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
            progress?.Report(new PingProgress(
                targets.Count,
                completed,
                results.Count(item => item.IsSuccess),
                results.Count(item => !item.IsSuccess),
                device.Id,
                result.IsSuccess ? DeviceStatus.Online : DeviceStatus.Offline,
                result.LatencyMs,
                result.CheckedAt));
        }

        return results;
    }
}
