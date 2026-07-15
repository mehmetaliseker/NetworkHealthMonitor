namespace NetworkHealthMonitor.Services;

public interface IAvailabilityRecalculationService
{
    Task RecalculateDeviceAsync(int deviceId, DateOnly startDate, DateOnly endDate, string timezoneId, CancellationToken cancellationToken = default);

    Task RecalculateGroupAsync(int groupId, DateOnly startDate, DateOnly endDate, string timezoneId, CancellationToken cancellationToken = default);

    Task RecalculateAllAsync(DateOnly startDate, DateOnly endDate, string timezoneId, CancellationToken cancellationToken = default);
}
