using NetworkHealthMonitor.Models;

namespace NetworkHealthMonitor.Services;

public interface INotificationPublisher
{
    Task<long> EnqueueAsync(
        string eventType,
        int? deviceId,
        long? incidentId,
        NtfyNotificationPayload payload,
        string deduplicationKey,
        CancellationToken cancellationToken = default);
}
