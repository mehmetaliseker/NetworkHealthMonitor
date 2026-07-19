namespace NetworkHealthMonitor.Services;

public interface IDeviceOutageIncidentRepository
{
    Task MarkNotificationSentAsync(
        long incidentId,
        string eventType,
        DateTime sentAtUtc,
        CancellationToken cancellationToken = default);
}
