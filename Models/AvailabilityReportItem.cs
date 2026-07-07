namespace NetworkHealthMonitor.Models;

public sealed class AvailabilityReportItem
{
    public int DeviceId { get; init; }

    public string DeviceName { get; init; } = string.Empty;

    public string IpAddress { get; init; } = string.Empty;

    public DeviceType DeviceType { get; init; }

    public string GroupName { get; init; } = string.Empty;

    public DeviceStatus LastStatus { get; init; }

    public DateTime? LastSuccessfulCheckAt { get; init; }

    public DateTime? LastFailedCheckAt { get; init; }

    public int TotalSuccessCount { get; init; }

    public int TotalFailureCount { get; init; }

    public double? MeasuredAvailabilityPercent { get; init; }

    public double? Availability24HoursPercent { get; init; }

    public double? Availability7DaysPercent { get; init; }

    public double? Availability30DaysPercent { get; init; }

    public int OutageCount { get; init; }

    public DateTime? LastOutageStartedAt { get; init; }

    public DateTime? LastRecoveryAt { get; init; }

    public TimeSpan EstimatedOutageDuration { get; init; }

    public string DeviceTypeText => DeviceType.ToDisplayName();

    public string LastStatusText => LastStatus.ToDisplayName();

    public string LastSuccessfulCheckAtText => FormatDate(LastSuccessfulCheckAt);

    public string LastFailedCheckAtText => FormatDate(LastFailedCheckAt);

    public string MeasuredAvailabilityText => FormatPercent(MeasuredAvailabilityPercent);

    public string Availability24HoursText => FormatPercent(Availability24HoursPercent);

    public string Availability7DaysText => FormatPercent(Availability7DaysPercent);

    public string Availability30DaysText => FormatPercent(Availability30DaysPercent);

    public string LastOutageStartedAtText => FormatDate(LastOutageStartedAt);

    public string LastRecoveryAtText => FormatDate(LastRecoveryAt);

    public string EstimatedOutageDurationText
    {
        get
        {
            if (EstimatedOutageDuration.TotalSeconds <= 0)
            {
                return "-";
            }

            if (EstimatedOutageDuration.TotalHours < 1)
            {
                return $"{EstimatedOutageDuration.TotalMinutes:0} dk";
            }

            return $"{EstimatedOutageDuration.TotalHours:0.0} sa";
        }
    }

    private static string FormatDate(DateTime? value)
    {
        return value.HasValue ? value.Value.ToString("dd.MM.yyyy HH:mm:ss") : "-";
    }

    private static string FormatPercent(double? value)
    {
        return value.HasValue ? $"{value.Value:0.00}%" : "-";
    }
}
