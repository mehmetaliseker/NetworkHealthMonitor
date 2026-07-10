using System.Globalization;

namespace NetworkHealthMonitor.Models;

public sealed class UptimeReportItem
{
    public int DeviceId { get; init; }

    public string DeviceName { get; init; } = string.Empty;

    public string IpAddress { get; init; } = string.Empty;

    public DeviceType DeviceType { get; init; }

    public string GroupName { get; init; } = string.Empty;

    public DeviceStatus HealthStatus { get; init; }

    public DateTime? LastCheckAt { get; init; }

    public DateTime? LastSuccessfulCheckAt { get; init; }

    public DateTime? LastFailedCheckAt { get; init; }

    public long? LatencyMs { get; init; }

    public int ConsecutiveFailureCount { get; init; }

    public int TotalChecks24h { get; init; }

    public int SuccessfulChecks24h { get; init; }

    public int FailedChecks24h { get; init; }

    public int TotalChecks7d { get; init; }

    public int SuccessfulChecks7d { get; init; }

    public int FailedChecks7d { get; init; }

    public int TotalChecks30d { get; init; }

    public int SuccessfulChecks30d { get; init; }

    public int FailedChecks30d { get; init; }

    public int TotalChecksOverall { get; init; }

    public int SuccessfulChecksOverall { get; init; }

    public int FailedChecksOverall { get; init; }

    public double? Uptime24hPercent => CalculateUptime(SuccessfulChecks24h, TotalChecks24h);

    public double? Uptime7dPercent => CalculateUptime(SuccessfulChecks7d, TotalChecks7d);

    public double? Uptime30dPercent => CalculateUptime(SuccessfulChecks30d, TotalChecks30d);

    public double? UptimeOverallPercent => CalculateUptime(SuccessfulChecksOverall, TotalChecksOverall);

    public string DeviceTypeText => DeviceType.ToDisplayName();

    public string HealthStatusText => HealthStatus.ToDisplayName();

    public string LastCheckAtText => FormatDate(LastCheckAt);

    public string LastSuccessfulCheckAtText => FormatDate(LastSuccessfulCheckAt);

    public string LastFailedCheckAtText => FormatDate(LastFailedCheckAt);

    public string LatencyMsText => LatencyMs?.ToString(CultureInfo.InvariantCulture) ?? string.Empty;

    private static double? CalculateUptime(int successCount, int totalCount)
    {
        return totalCount == 0 ? null : successCount * 100d / totalCount;
    }

    private static string FormatDate(DateTime? value)
    {
        return value.HasValue
            ? value.Value.ToString("dd.MM.yyyy HH:mm:ss", CultureInfo.CurrentCulture)
            : string.Empty;
    }
}
