namespace NetworkHealthMonitor.Models;

public sealed class NotificationOutboxItem
{
    public long Id { get; set; }

    public string EventType { get; set; } = string.Empty;

    public int? DeviceId { get; set; }

    public string DeviceName { get; set; } = string.Empty;

    public long? IncidentId { get; set; }

    public string PayloadJson { get; set; } = string.Empty;

    public string DeduplicationKey { get; set; } = string.Empty;

    public string Status { get; set; } = "Pending";

    public int AttemptCount { get; set; }

    public DateTime NextAttemptAtUtc { get; set; }

    public DateTime? LockedAtUtc { get; set; }

    public DateTime? LastAttemptAtUtc { get; set; }

    public string LockedBy { get; set; } = string.Empty;

    public string LastError { get; set; } = string.Empty;

    public DateTime CreatedAtUtc { get; set; }

    public DateTime? SentAtUtc { get; set; }

    public DateTime? CancelledAtUtc { get; set; }

    public bool IsSelected { get; set; }

    public string CreatedAtText => CreatedAtUtc.ToLocalTime().ToString("dd.MM.yyyy HH:mm:ss");

    public string SentAtText => SentAtUtc.HasValue ? SentAtUtc.Value.ToLocalTime().ToString("dd.MM.yyyy HH:mm:ss") : "-";

    public string LastAttemptAtText => LastAttemptAtUtc.HasValue ? LastAttemptAtUtc.Value.ToLocalTime().ToString("dd.MM.yyyy HH:mm:ss") : "-";

    public string NextAttemptAtText => NextAttemptAtUtc.ToLocalTime().ToString("dd.MM.yyyy HH:mm:ss");

    public string IncidentText => IncidentId.HasValue ? IncidentId.Value.ToString() : "-";

    public string StatusText => UiDisplayTexts.OutboxStatus(Status);

    public string EventTypeText => UiDisplayTexts.OutboxEventType(EventType);
}
