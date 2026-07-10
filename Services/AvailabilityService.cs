using NetworkHealthMonitor.Data;
using NetworkHealthMonitor.Models;

namespace NetworkHealthMonitor.Services;

public sealed class AvailabilityService : IAvailabilityService
{
    private readonly DeviceRepository _deviceRepository;
    private readonly PingLogRepository _pingLogRepository;
    private readonly OutageRepository _outageRepository;

    public AvailabilityService(
        DeviceRepository deviceRepository,
        PingLogRepository pingLogRepository,
        OutageRepository outageRepository)
    {
        _deviceRepository = deviceRepository;
        _pingLogRepository = pingLogRepository;
        _outageRepository = outageRepository;
    }

    public async Task<IReadOnlyList<AvailabilityReportItem>> GetDeviceAvailabilityAsync(DateTime since)
    {
        var devices = await _deviceRepository.GetAllAsync();
        var metrics = await _pingLogRepository.GetAvailabilityMetricsAsync(since);
        var outages = await _outageRepository.GetByDeviceSinceAsync(since);
        var now = DateTime.Now;

        return devices
            .OrderBy(device => device.Name)
            .Select(device =>
            {
                metrics.TryGetValue(device.Id, out var metric);
                outages.TryGetValue(device.Id, out var deviceOutages);
                deviceOutages ??= Array.Empty<Outage>();

                var lastOutage = deviceOutages.OrderByDescending(outage => outage.StartedAt).FirstOrDefault();

                return new AvailabilityReportItem
                {
                    DeviceId = device.Id,
                    DeviceName = device.Name,
                    IpAddress = device.IpAddress,
                    DeviceType = device.DeviceType,
                    GroupName = device.GroupName,
                    LastStatus = device.LastStatus,
                    LastSuccessfulCheckAt = metric?.LastSuccessfulCheckAt ?? device.LastSuccessfulCheckAt,
                    LastFailedCheckAt = metric?.LastFailedCheckAt ?? device.LastFailedCheckAt,
                    TotalSuccessCount = metric?.TotalSuccessCount ?? 0,
                    TotalFailureCount = metric?.TotalFailureCount ?? 0,
                    MeasuredAvailabilityPercent = metric?.MeasuredAvailabilityPercent,
                    Availability24HoursPercent = metric?.Availability24HoursPercent,
                    Availability7DaysPercent = metric?.Availability7DaysPercent,
                    Availability30DaysPercent = metric?.Availability30DaysPercent,
                    AvailabilityOverallPercent = metric?.AvailabilityOverallPercent,
                    OutageCount = deviceOutages.Count,
                    LastOutageStartedAt = lastOutage?.StartedAt,
                    LastRecoveryAt = deviceOutages
                        .Where(outage => outage.EndedAt.HasValue)
                        .OrderByDescending(outage => outage.EndedAt)
                        .Select(outage => outage.EndedAt)
                        .FirstOrDefault(),
                    EstimatedOutageDuration = CalculateOutageDuration(deviceOutages, now)
                };
            })
            .ToList();
    }

    private static TimeSpan CalculateOutageDuration(IEnumerable<Outage> outages, DateTime now)
    {
        return outages.Aggregate(
            TimeSpan.Zero,
            (total, outage) => total + ((outage.EndedAt ?? now) - outage.StartedAt));
    }
}
