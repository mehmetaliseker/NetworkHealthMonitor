namespace NetworkHealthMonitor.Models;

public sealed class NtfyNotificationPayload
{
    public string EventType { get; set; } = string.Empty;

    public string Title { get; set; } = string.Empty;

    public string Message { get; set; } = string.Empty;

    public string Priority { get; set; } = "default";

    public string Tags { get; set; } = string.Empty;

    public int? DeviceId { get; set; }

    public long? IncidentId { get; set; }
}
