using System.Collections.Concurrent;
using NetworkHealthMonitor.Models;

namespace NetworkHealthMonitor.Services;

public sealed class AcceptancePingService : IPingService
{
    private readonly IPingService _inner;
    private readonly AppSettingsService _settingsService;

    public AcceptancePingService(IPingService inner, AppSettingsService settingsService)
    {
        _inner = inner;
        _settingsService = settingsService;
    }

    public async Task<PingDeviceResult> PingAsync(
        Device device,
        PingOptions options,
        CancellationToken cancellationToken = default)
    {
        var mode = await ResolveModeAsync(device, cancellationToken);
        if (mode is null)
        {
            return await _inner.PingAsync(device, options, cancellationToken);
        }

        var checkedAt = DateTime.Now;
        return mode switch
        {
            AcceptancePingMode.Online => new PingDeviceResult(
                device,
                true,
                1,
                checkedAt,
                "Acceptance ping success (1 ms)",
                string.Empty),
            AcceptancePingMode.Timeout => await TimeoutAsync(device, options, checkedAt, cancellationToken),
            _ => new PingDeviceResult(
                device,
                false,
                null,
                checkedAt,
                "Acceptance ping failure",
                "host unreachable")
        };
    }

    public async Task<IReadOnlyList<PingDeviceResult>> PingManyAsync(
        IEnumerable<Device> devices,
        PingOptions options,
        IProgress<PingProgress>? progress,
        CancellationToken cancellationToken = default)
    {
        var targets = devices.DistinctBy(device => device.Id).ToList();
        if (targets.Count == 0)
        {
            return Array.Empty<PingDeviceResult>();
        }

        var results = new ConcurrentBag<PingDeviceResult>();
        var completed = 0;
        var success = 0;
        var failure = 0;
        using var gate = new SemaphoreSlim(options.MaxParallelPings);

        var tasks = targets.Select(async device =>
        {
            await gate.WaitAsync(cancellationToken);
            try
            {
                progress?.Report(new PingProgress(
                    targets.Count,
                    Volatile.Read(ref completed),
                    Volatile.Read(ref success),
                    Volatile.Read(ref failure),
                    device.Id,
                    DeviceStatus.Checking));

                var result = await PingAsync(device, options, cancellationToken);
                results.Add(result);
                if (result.IsSuccess)
                {
                    Interlocked.Increment(ref success);
                }
                else
                {
                    Interlocked.Increment(ref failure);
                }

                var done = Interlocked.Increment(ref completed);
                progress?.Report(new PingProgress(
                    targets.Count,
                    done,
                    Volatile.Read(ref success),
                    Volatile.Read(ref failure),
                    device.Id,
                    result.Status,
                    result.LatencyMs,
                    result.CheckedAt));
            }
            finally
            {
                gate.Release();
            }
        });

        await Task.WhenAll(tasks);
        return results.OrderBy(result => result.CheckedAt).ToList();
    }

    private async Task<AcceptancePingMode?> ResolveModeAsync(Device device, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var acceptance = (await _settingsService.LoadAsync()).AcceptanceTest;
        if (!acceptance.Enabled)
        {
            return null;
        }

        var nameMatches = !string.IsNullOrWhiteSpace(acceptance.DeviceNamePrefix)
            && device.Name.StartsWith(acceptance.DeviceNamePrefix, StringComparison.OrdinalIgnoreCase);
        var probeText = string.Join(
            " ",
            device.Name,
            device.IpAddress,
            device.GroupName,
            device.Location,
            device.Description).ToLowerInvariant();
        var tokenMatches = probeText.Contains(AcceptanceTestSettings.OnlineToken, StringComparison.Ordinal)
            || probeText.Contains(AcceptanceTestSettings.OfflineToken, StringComparison.Ordinal)
            || probeText.Contains(AcceptanceTestSettings.TimeoutToken, StringComparison.Ordinal);

        if (!nameMatches && !tokenMatches)
        {
            return null;
        }

        if (probeText.Contains(AcceptanceTestSettings.OnlineToken, StringComparison.Ordinal))
        {
            return AcceptancePingMode.Online;
        }

        if (probeText.Contains(AcceptanceTestSettings.TimeoutToken, StringComparison.Ordinal))
        {
            return AcceptancePingMode.Timeout;
        }

        if (probeText.Contains(AcceptanceTestSettings.OfflineToken, StringComparison.Ordinal))
        {
            return AcceptancePingMode.Offline;
        }

        return null;
    }

    private static async Task<PingDeviceResult> TimeoutAsync(
        Device device,
        PingOptions options,
        DateTime checkedAt,
        CancellationToken cancellationToken)
    {
        await Task.Delay(options.TimeoutMs, cancellationToken);
        return new PingDeviceResult(
            device,
            false,
            null,
            checkedAt,
            $"Acceptance ping timeout after {options.TimeoutMs} ms",
            "timeout");
    }

    private enum AcceptancePingMode
    {
        Online,
        Offline,
        Timeout
    }
}
