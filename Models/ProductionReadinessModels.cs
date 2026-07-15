using System.Globalization;

namespace NetworkHealthMonitor.Models;

public enum ReadinessLevel
{
    Pass = 0,
    Warning = 1,
    Fail = 2
}

public sealed class ReadinessCheckItem
{
    public string Name { get; init; } = string.Empty;

    public ReadinessLevel Level { get; init; }

    public string Value { get; init; } = string.Empty;

    public string Detail { get; init; } = string.Empty;

    public string LevelText => Level switch
    {
        ReadinessLevel.Pass => "PASS",
        ReadinessLevel.Warning => "WARN",
        _ => "FAIL"
    };
}

public sealed class ServiceReadinessSnapshot
{
    public IReadOnlyList<ReadinessCheckItem> Checks { get; init; } = Array.Empty<ReadinessCheckItem>();

    public IReadOnlyList<ReadinessCheckItem> Diagnostics { get; init; } = Array.Empty<ReadinessCheckItem>();

    public bool IsHealthy => Checks.All(check => check.Level != ReadinessLevel.Fail);

    public string OverallStatusText => IsHealthy ? "Healthy" : "Unhealthy";
}

public sealed class AvailabilityTrendPoint
{
    public DateOnly Date { get; init; }

    public long UpSeconds { get; init; }

    public long DownSeconds { get; init; }

    public long UnknownSeconds { get; init; }

    public long MaintenanceSeconds { get; init; }

    public long ExpectedMonitoringSeconds { get; init; }

    public double? AvailabilityPercent { get; init; }

    public double? CoveragePercent { get; init; }

    public string DateText => Date.ToString("dd.MM", CultureInfo.CurrentCulture);

    public string AvailabilityText => FormatPercent(AvailabilityPercent);

    public string CoverageText => FormatPercent(CoveragePercent);

    public string UnknownText => AvailabilitySummaryReportItem.FormatDuration(UnknownSeconds);

    private static string FormatPercent(double? value)
    {
        return value.HasValue ? $"{value.Value:0.00}%" : "-";
    }
}

public sealed class AvailabilityRankingRow
{
    public string Title { get; init; } = string.Empty;

    public string Subtitle { get; init; } = string.Empty;

    public string Value { get; init; } = string.Empty;

    public double Percent { get; init; }
}

public sealed class DeviceAvailabilityDetail
{
    public int DeviceId { get; init; }

    public string DeviceName { get; init; } = string.Empty;

    public string CurrentStatus { get; init; } = "-";

    public string CurrentStatusSince { get; init; } = "-";

    public string CurrentContinuousAvailability { get; init; } = "-";

    public string OngoingDowntime { get; init; } = "-";

    public string FirstFailureAt { get; init; } = "-";

    public string ConfirmedDownAt { get; init; } = "-";

    public string LastSuccessfulCheck { get; init; } = "-";

    public string LastCheck { get; init; } = "-";

    public string Availability24Hours { get; init; } = "-";

    public string Availability7Days { get; init; } = "-";

    public string Availability30Days { get; init; } = "-";

    public string Coverage { get; init; } = "-";

    public string IncidentCount { get; init; } = "0";

    public string Mttr { get; init; } = "-";

    public string Mtbf { get; init; } = "-";

    public string SlaTarget { get; init; } = "-";

    public string SlaStatus { get; init; } = "-";
}
