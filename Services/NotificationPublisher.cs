using System.Text.Json;
using NetworkHealthMonitor.Models;

namespace NetworkHealthMonitor.Services;

public sealed class NotificationPublisher : INotificationPublisher
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private readonly INotificationOutboxRepository _outboxRepository;

    public NotificationPublisher(INotificationOutboxRepository outboxRepository)
    {
        _outboxRepository = outboxRepository;
    }

    public Task<long> EnqueueAsync(
        string eventType,
        int? deviceId,
        long? incidentId,
        NtfyNotificationPayload payload,
        string deduplicationKey,
        CancellationToken cancellationToken = default)
    {
        payload.EventType = eventType;
        payload.DeviceId = deviceId;
        payload.IncidentId = incidentId;
        return _outboxRepository.AddPendingAsync(
            eventType,
            deviceId,
            incidentId,
            JsonSerializer.Serialize(payload, JsonOptions),
            deduplicationKey,
            DateTime.UtcNow,
            cancellationToken);
    }
}
