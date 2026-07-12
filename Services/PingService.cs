using System.Collections.Concurrent;
using System.Net.NetworkInformation;
using NetworkHealthMonitor.Models;
using PingOptions = NetworkHealthMonitor.Models.PingOptions;

namespace NetworkHealthMonitor.Services;

public sealed class PingService : IPingService
{
    public async Task<PingDeviceResult> PingAsync(
        Device device,
        PingOptions options,
        CancellationToken cancellationToken = default)
    {
        var checkedAt = DateTime.Now;

        try
        {
            using var ping = new Ping();
            var reply = await ping.SendPingAsync(device.IpAddress, options.TimeoutMs).WaitAsync(cancellationToken);

            if (reply.Status == IPStatus.Success)
            {
                return new PingDeviceResult(
                    device,
                    true,
                    reply.RoundtripTime,
                    checkedAt,
                    $"Yanıt alındı ({reply.RoundtripTime} ms)",
                    string.Empty);
            }

            var statusText = TranslateStatus(reply.Status);
            return new PingDeviceResult(
                device,
                false,
                null,
                checkedAt,
                $"Yanıt alınamadı: {statusText}",
                statusText);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (PingException ex)
        {
            return new PingDeviceResult(
                device,
                false,
                null,
                checkedAt,
                "Ping hatası oluştu.",
                ex.InnerException?.Message ?? ex.Message);
        }
        catch (Exception ex)
        {
            return new PingDeviceResult(
                device,
                false,
                null,
                checkedAt,
                "Beklenmeyen hata oluştu.",
                ex.Message);
        }
    }

    public async Task<IReadOnlyList<PingDeviceResult>> PingManyAsync(
        IEnumerable<Device> devices,
        PingOptions options,
        IProgress<PingProgress>? progress,
        CancellationToken cancellationToken = default)
    {
        var targets = devices.DistinctBy(device => device.Id).ToList();
        var results = new ConcurrentBag<PingDeviceResult>();
        var total = targets.Count;
        var completed = 0;
        var success = 0;
        var failure = 0;

        if (total == 0)
        {
            return Array.Empty<PingDeviceResult>();
        }

        var parallelOptions = new ParallelOptions
        {
            MaxDegreeOfParallelism = options.MaxParallelPings,
            CancellationToken = cancellationToken
        };

        await Parallel.ForEachAsync(targets, parallelOptions, async (device, token) =>
        {
            progress?.Report(new PingProgress(
                total,
                Volatile.Read(ref completed),
                Volatile.Read(ref success),
                Volatile.Read(ref failure),
                device.Id,
                DeviceStatus.Checking));

            var result = await PingAsync(device, options, token);
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
                total,
                done,
                Volatile.Read(ref success),
                Volatile.Read(ref failure),
                device.Id,
                result.Status,
                result.LatencyMs,
                result.CheckedAt));
        });

        return results.OrderBy(result => result.CheckedAt).ToList();
    }

    private static string TranslateStatus(IPStatus status)
    {
        return status switch
        {
            IPStatus.TimedOut => "Zaman aşımı",
            IPStatus.DestinationHostUnreachable => "Hedef cihaza ulaşılamıyor",
            IPStatus.DestinationNetworkUnreachable => "Ağa ulaşılamıyor",
            IPStatus.DestinationPortUnreachable => "Hedef porta ulaşılamıyor",
            IPStatus.BadDestination => "Hatalı hedef",
            IPStatus.BadRoute => "Hatalı rota",
            IPStatus.PacketTooBig => "Paket çok büyük",
            IPStatus.TtlExpired => "TTL süresi doldu",
            IPStatus.Unknown => "Bilinmeyen durum",
            _ => status.ToString()
        };
    }
}
