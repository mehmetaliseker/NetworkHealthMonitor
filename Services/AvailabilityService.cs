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
        var logs = await _pingLogRepository.GetForAvailabilityAsync(since);
        var outages = await _outageRepository.GetByDeviceSinceAsync(since);
        var now = DateTime.Now;

        var logsByDevice = logs
            .Where(log => log.DeviceId.HasValue)
            .GroupBy(log => log.DeviceId!.Value)
            .ToDictionary(group => group.Key, group => group.OrderByDescending(log => log.CheckedAt).ToList());

        return devices
            .OrderBy(device => device.Name)
            .Select(device =>
            {
                logsByDevice.TryGetValue(device.Id, out var deviceLogs);
                deviceLogs ??= new List<PingLog>();
                outages.TryGetValue(device.Id, out var deviceOutages);
                deviceOutages ??= Array.Empty<Outage>();

                var successCount = deviceLogs.Count(log => log.Status == DeviceStatus.Reachable);
                var failureCount = deviceLogs.Count(log => log.Status == DeviceStatus.Unreachable);
                var lastOutage = deviceOutages.OrderByDescending(outage => outage.StartedAt).FirstOrDefault();

                return new AvailabilityReportItem
                {
                    DeviceId = device.Id,
                    DeviceName = device.Name,
                    IpAddress = device.IpAddress,
                    DeviceType = device.DeviceType,
                    GroupName = device.GroupName,
                    LastStatus = device.LastStatus,
                    LastSuccessfulCheckAt = deviceLogs
                        .Where(log => log.Status == DeviceStatus.Reachable)
                        .Select(log => (DateTime?)log.CheckedAt)
                        .FirstOrDefault(),
                    LastFailedCheckAt = deviceLogs
                        .Where(log => log.Status == DeviceStatus.Unreachable)
                        .Select(log => (DateTime?)log.CheckedAt)
                        .FirstOrDefault(),
                    TotalSuccessCount = successCount,
                    TotalFailureCount = failureCount,
                    MeasuredAvailabilityPercent = CalculateAvailability(successCount, failureCount),
                    Availability24HoursPercent = CalculateAvailability(deviceLogs, now.AddHours(-24)),
                    Availability7DaysPercent = CalculateAvailability(deviceLogs, now.AddDays(-7)),
                    Availability30DaysPercent = CalculateAvailability(deviceLogs, now.AddDays(-30)),
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

    private static double? CalculateAvailability(IReadOnlyCollection<PingLog> logs, DateTime since)
    {
        var scoped = logs.Where(log => log.CheckedAt >= since).ToList();
        if (scoped.Count == 0)
        {
            return null;
        }

        return scoped.Count(log => log.Status == DeviceStatus.Reachable) * 100d / scoped.Count;
    }

    private static double? CalculateAvailability(int successCount, int failureCount)
    {
        var total = successCount + failureCount;
        return total == 0 ? null : successCount * 100d / total;
    }

    private static TimeSpan CalculateOutageDuration(IEnumerable<Outage> outages, DateTime now)
    {
        return outages.Aggregate(
            TimeSpan.Zero,
            (total, outage) => total + ((outage.EndedAt ?? now) - outage.StartedAt));
    }
}
