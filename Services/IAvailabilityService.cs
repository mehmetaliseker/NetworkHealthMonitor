using NetworkHealthMonitor.Models;

namespace NetworkHealthMonitor.Services;

public interface IAvailabilityService
{
    Task<IReadOnlyList<AvailabilityReportItem>> GetDeviceAvailabilityAsync(DateTime since);

    Task<IReadOnlyList<AvailabilitySummaryReportItem>> GetAvailabilitySummaryAsync(
        DateTime startUtc,
        DateTime endUtc,
        string timezoneId,
        int? groupId = null,
        bool includeDeleted = false,
        CancellationToken cancellationToken = default);

    Task<AvailabilityDashboardSummary> GetDashboardSummaryAsync(DateTime nowUtc, CancellationToken cancellationToken = default);

    Task RecalculateDailyAsync(
        DateOnly startDate,
        DateOnly endDate,
        string timezoneId,
        int? deviceId = null,
        int? groupId = null,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<DeviceAvailabilityPeriod>> GetTimelineAsync(
        int deviceId,
        DateTime startUtc,
        DateTime endUtc,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<AvailabilityIncidentReportItem>> GetIncidentReportAsync(
        DateTime startUtc,
        DateTime endUtc,
        int? deviceId = null,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<AvailabilityTrendPoint>> GetDailyTrendAsync(
        DateTime startUtc,
        DateTime endUtc,
        string timezoneId,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<AvailabilityRankingRow>> GetLongestOutagesAsync(
        DateTime startUtc,
        DateTime endUtc,
        int limit,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<AvailabilityRankingRow>> GetIncidentRankingAsync(
        DateTime startUtc,
        DateTime endUtc,
        int limit,
        CancellationToken cancellationToken = default);
}
