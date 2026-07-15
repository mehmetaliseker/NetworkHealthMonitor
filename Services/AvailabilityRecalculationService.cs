using NetworkHealthMonitor.Data;

namespace NetworkHealthMonitor.Services;

public sealed class AvailabilityRecalculationService : IAvailabilityRecalculationService
{
    private readonly AvailabilityRepository _availabilityRepository;

    public AvailabilityRecalculationService(AvailabilityRepository availabilityRepository)
    {
        _availabilityRepository = availabilityRepository;
    }

    public Task RecalculateDeviceAsync(
        int deviceId,
        DateOnly startDate,
        DateOnly endDate,
        string timezoneId,
        CancellationToken cancellationToken = default)
    {
        return _availabilityRepository.RecalculateDailyAsync(startDate, endDate, timezoneId, deviceId, null, cancellationToken);
    }

    public Task RecalculateGroupAsync(
        int groupId,
        DateOnly startDate,
        DateOnly endDate,
        string timezoneId,
        CancellationToken cancellationToken = default)
    {
        return _availabilityRepository.RecalculateDailyAsync(startDate, endDate, timezoneId, null, groupId, cancellationToken);
    }

    public Task RecalculateAllAsync(
        DateOnly startDate,
        DateOnly endDate,
        string timezoneId,
        CancellationToken cancellationToken = default)
    {
        return _availabilityRepository.RecalculateDailyAsync(startDate, endDate, timezoneId, null, null, cancellationToken);
    }
}
