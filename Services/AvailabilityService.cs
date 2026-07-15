using NetworkHealthMonitor.Data;
using NetworkHealthMonitor.Models;

namespace NetworkHealthMonitor.Services;

public sealed class AvailabilityService : IAvailabilityService
{
    private readonly AvailabilityRepository _availabilityRepository;

    public AvailabilityService(AvailabilityRepository availabilityRepository)
    {
        _availabilityRepository = availabilityRepository;
    }

    public AvailabilityService(
        DeviceRepository deviceRepository,
        PingLogRepository pingLogRepository,
        OutageRepository outageRepository)
        : this(new AvailabilityRepository(deviceRepository.ConnectionFactory))
    {
        _ = pingLogRepository;
        _ = outageRepository;
    }

    public async Task<IReadOnlyList<AvailabilityReportItem>> GetDeviceAvailabilityAsync(DateTime since)
    {
        var endUtc = DateTime.UtcNow;
        var startUtc = since.Kind == DateTimeKind.Utc ? since : since.ToUniversalTime();
        var summary = await _availabilityRepository.GetSummaryAsync(startUtc, endUtc, TimeZoneInfo.Local.Id);

        return summary
            .Select(item => new AvailabilityReportItem
            {
                DeviceId = item.DeviceId,
                DeviceName = item.DeviceName,
                IpAddress = item.IpAddress,
                DeviceType = item.DeviceType,
                GroupName = item.GroupName,
                LastStatus = ToDeviceStatus(item.CurrentStatus),
                LastSuccessfulCheckAt = item.LastSuccessfulCheckAtUtc?.ToLocalTime(),
                LastFailedCheckAt = null,
                TotalSuccessCount = 0,
                TotalFailureCount = 0,
                MeasuredAvailabilityPercent = item.AvailabilityPercent,
                Availability24HoursPercent = item.AvailabilityPercent,
                Availability7DaysPercent = item.AvailabilityPercent,
                Availability30DaysPercent = item.AvailabilityPercent,
                AvailabilityOverallPercent = item.AvailabilityPercent,
                OutageCount = item.IncidentCount,
                LastOutageStartedAt = item.CurrentStatus == AvailabilityStatus.Down ? item.CurrentStatusSinceUtc?.ToLocalTime() : null,
                LastRecoveryAt = null,
                EstimatedOutageDuration = TimeSpan.FromSeconds(item.DownSeconds),
                CurrentAvailabilityStatus = item.CurrentStatus,
                CurrentStatusSinceUtc = item.CurrentStatusSinceUtc,
                CurrentContinuousAvailabilitySeconds = item.CurrentContinuousAvailabilitySeconds,
                UpSeconds = item.UpSeconds,
                DownSeconds = item.DownSeconds,
                UnknownSeconds = item.UnknownSeconds,
                MaintenanceSeconds = item.MaintenanceSeconds,
                StrictAvailabilityPercent = item.StrictAvailabilityPercent,
                CoveragePercent = item.CoveragePercent
            })
            .ToList();
    }

    public Task<IReadOnlyList<AvailabilitySummaryReportItem>> GetAvailabilitySummaryAsync(
        DateTime startUtc,
        DateTime endUtc,
        string timezoneId,
        int? groupId = null,
        bool includeDeleted = false,
        CancellationToken cancellationToken = default)
    {
        return _availabilityRepository.GetSummaryAsync(startUtc, endUtc, timezoneId, groupId, includeDeleted, cancellationToken);
    }

    public Task<AvailabilityDashboardSummary> GetDashboardSummaryAsync(DateTime nowUtc, CancellationToken cancellationToken = default)
    {
        return _availabilityRepository.GetDashboardSummaryAsync(nowUtc, cancellationToken);
    }

    public Task RecalculateDailyAsync(
        DateOnly startDate,
        DateOnly endDate,
        string timezoneId,
        int? deviceId = null,
        int? groupId = null,
        CancellationToken cancellationToken = default)
    {
        return _availabilityRepository.RecalculateDailyAsync(startDate, endDate, timezoneId, deviceId, groupId, cancellationToken);
    }

    public Task<IReadOnlyList<DeviceAvailabilityPeriod>> GetTimelineAsync(
        int deviceId,
        DateTime startUtc,
        DateTime endUtc,
        CancellationToken cancellationToken = default)
    {
        return _availabilityRepository.GetPeriodsAsync(deviceId, startUtc, endUtc, cancellationToken);
    }

    public Task<IReadOnlyList<AvailabilityIncidentReportItem>> GetIncidentReportAsync(
        DateTime startUtc,
        DateTime endUtc,
        int? deviceId = null,
        CancellationToken cancellationToken = default)
    {
        return _availabilityRepository.GetIncidentReportAsync(startUtc, endUtc, deviceId, cancellationToken);
    }

    public Task<IReadOnlyList<AvailabilityTrendPoint>> GetDailyTrendAsync(
        DateTime startUtc,
        DateTime endUtc,
        string timezoneId,
        CancellationToken cancellationToken = default)
    {
        return _availabilityRepository.GetDailyTrendAsync(startUtc, endUtc, timezoneId, cancellationToken);
    }

    public Task<IReadOnlyList<AvailabilityRankingRow>> GetLongestOutagesAsync(
        DateTime startUtc,
        DateTime endUtc,
        int limit,
        CancellationToken cancellationToken = default)
    {
        return _availabilityRepository.GetLongestOutagesAsync(startUtc, endUtc, limit, cancellationToken);
    }

    public Task<IReadOnlyList<AvailabilityRankingRow>> GetIncidentRankingAsync(
        DateTime startUtc,
        DateTime endUtc,
        int limit,
        CancellationToken cancellationToken = default)
    {
        return _availabilityRepository.GetIncidentRankingAsync(startUtc, endUtc, limit, cancellationToken);
    }

    private static DeviceStatus ToDeviceStatus(AvailabilityStatus status)
    {
        return status switch
        {
            AvailabilityStatus.Up => DeviceStatus.Online,
            AvailabilityStatus.Down => DeviceStatus.Offline,
            AvailabilityStatus.Maintenance => DeviceStatus.UnderWatch,
            AvailabilityStatus.Paused => DeviceStatus.Unknown,
            _ => DeviceStatus.Unknown
        };
    }
}
