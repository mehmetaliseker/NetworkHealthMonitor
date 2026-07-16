using System.Globalization;

namespace NetworkHealthMonitor.Models;

public enum AvailabilityStatus
{
    Unknown = 0,
    Up = 1,
    SuspectedDown = 2,
    Down = 3,
    SuspectedRecovery = 4,
    Maintenance = 5,
    Paused = 6
}

public static class AvailabilityStatusExtensions
{
    public static string ToStorageValue(this AvailabilityStatus status)
    {
        return status.ToString();
    }

    public static AvailabilityStatus FromStorageValue(string? value)
    {
        return Enum.TryParse<AvailabilityStatus>(value, true, out var parsed)
            ? parsed
            : AvailabilityStatus.Unknown;
    }

    public static string ToDisplayName(this AvailabilityStatus status)
    {
        return status switch
        {
            AvailabilityStatus.Up => "Erişilebilir",
            AvailabilityStatus.SuspectedDown => "Kesinti şüpheli",
            AvailabilityStatus.Down => "Kesinti",
            AvailabilityStatus.SuspectedRecovery => "Düzelme şüpheli",
            AvailabilityStatus.Maintenance => "Planlı bakım",
            AvailabilityStatus.Paused => "İzleme duraklatıldı",
            AvailabilityStatus.Unknown => "Kontrol edilmedi",
            _ => "Kontrol edilmedi"
        };
    }

    public static bool IsReportStatus(this AvailabilityStatus status)
    {
        return status is AvailabilityStatus.Up
            or AvailabilityStatus.Down
            or AvailabilityStatus.Unknown
            or AvailabilityStatus.Maintenance
            or AvailabilityStatus.Paused;
    }
}

public enum DowntimeStartPolicy
{
    FirstFailedCheck = 0,
    ConfirmedDownTime = 1
}

public sealed class DeviceAvailabilityPeriod
{
    public long Id { get; init; }

    public int DeviceId { get; init; }

    public AvailabilityStatus Status { get; init; }

    public DateTime StartedAtUtc { get; init; }

    public DateTime? EndedAtUtc { get; init; }

    public long? DurationSeconds { get; init; }

    public long? IncidentId { get; init; }

    public string ReasonCode { get; init; } = string.Empty;

    public string ReasonText { get; init; } = string.Empty;

    public string DetectionSource { get; init; } = string.Empty;

    public DateTime? FirstFailureAtUtc { get; init; }

    public DateTime? ConfirmedAtUtc { get; init; }

    public DateTime CreatedAtUtc { get; init; }

    public DateTime UpdatedAtUtc { get; init; }

    public long EffectiveDurationSeconds(DateTime endUtc)
    {
        var end = EndedAtUtc ?? endUtc;
        return Math.Max(0, (long)Math.Round((end.ToUniversalTime() - StartedAtUtc.ToUniversalTime()).TotalSeconds));
    }

    public string StatusText => Status.ToDisplayName();

    public string StartedAtText => StartedAtUtc.ToLocalTime().ToString("dd.MM.yyyy HH:mm:ss", CultureInfo.CurrentCulture);

    public string EndedAtText => EndedAtUtc.HasValue
        ? EndedAtUtc.Value.ToLocalTime().ToString("dd.MM.yyyy HH:mm:ss", CultureInfo.CurrentCulture)
        : "Devam ediyor";
}

public sealed class DeviceAvailabilityDaily
{
    public long Id { get; init; }

    public int DeviceId { get; init; }

    public DateOnly Date { get; init; }

    public string TimezoneId { get; init; } = TimeZoneInfo.Local.Id;

    public long ExpectedMonitoringSeconds { get; init; }

    public long UpSeconds { get; init; }

    public long DownSeconds { get; init; }

    public long UnknownSeconds { get; init; }

    public long MaintenanceSeconds { get; init; }

    public long PausedSeconds { get; init; }

    public int IncidentCount { get; init; }

    public int RecoveredIncidentCount { get; init; }

    public long LongestOutageSeconds { get; init; }

    public long TotalDetectionDelaySeconds { get; init; }

    public double? AvailabilityPercent { get; init; }

    public double? StrictAvailabilityPercent { get; init; }

    public double? CoveragePercent { get; init; }

    public DateTime CalculatedAtUtc { get; init; }

    public int CalculationVersion { get; init; }
}

public sealed class AvailabilitySummaryReportItem
{
    public int DeviceId { get; init; }

    public string DeviceName { get; init; } = string.Empty;

    public string IpAddress { get; init; } = string.Empty;

    public DeviceType DeviceType { get; init; }

    public string GroupName { get; init; } = string.Empty;

    public DateTime ReportStartUtc { get; init; }

    public DateTime ReportEndUtc { get; init; }

    public string TimezoneId { get; init; } = TimeZoneInfo.Local.Id;

    public long ExpectedMonitoringSeconds { get; init; }

    public long UpSeconds { get; init; }

    public long DownSeconds { get; init; }

    public long UnknownSeconds { get; init; }

    public long MaintenanceSeconds { get; init; }

    public long PausedSeconds { get; init; }

    public int IncidentCount { get; init; }

    public int RecoveredIncidentCount { get; init; }

    public long MttrSeconds { get; init; }

    public long MtbfSeconds { get; init; }

    public long LongestOutageSeconds { get; init; }

    public long TotalDetectionDelaySeconds { get; init; }

    public double? AvailabilityPercent { get; init; }

    public double? StrictAvailabilityPercent { get; init; }

    public double? CoveragePercent { get; init; }

    public AvailabilityStatus CurrentStatus { get; init; } = AvailabilityStatus.Unknown;

    public DateTime? CurrentStatusSinceUtc { get; init; }

    public long CurrentContinuousAvailabilitySeconds { get; init; }

    public DateTime? LastCheckedAtUtc { get; init; }

    public DateTime? LastSuccessfulCheckAtUtc { get; init; }

    public double? SlaTargetPercent { get; init; }

    public string SlaStatus { get; init; } = "Tanimli degil";

    public string DeviceTypeText => DeviceType.ToDisplayName();

    public string CurrentStatusText => CurrentStatus.ToDisplayName();

    public string AvailabilityText => FormatPercent(AvailabilityPercent);

    public string StrictAvailabilityText => FormatPercent(StrictAvailabilityPercent);

    public string CoverageText => FormatPercent(CoveragePercent);

    public string CurrentContinuousAvailabilityText => FormatDuration(CurrentContinuousAvailabilitySeconds);

    public static string FormatDuration(long seconds)
    {
        if (seconds <= 0)
        {
            return "0 sn";
        }

        var value = TimeSpan.FromSeconds(seconds);
        if (value.TotalDays >= 1)
        {
            return $"{(int)value.TotalDays} gun {value.Hours} sa {value.Minutes} dk";
        }

        if (value.TotalHours >= 1)
        {
            return $"{(int)value.TotalHours} sa {value.Minutes} dk";
        }

        if (value.TotalMinutes >= 1)
        {
            return $"{(int)value.TotalMinutes} dk {value.Seconds} sn";
        }

        return $"{value.Seconds} sn";
    }

    public static string FormatPercent(double? value)
    {
        return value.HasValue ? value.Value.ToString("0.000", CultureInfo.CurrentCulture) : string.Empty;
    }
}

public sealed class AvailabilityIncidentReportItem
{
    public long IncidentId { get; init; }

    public int DeviceId { get; init; }

    public string DeviceName { get; init; } = string.Empty;

    public string IpAddress { get; init; } = string.Empty;

    public string GroupName { get; init; } = string.Empty;

    public DateTime FirstFailureAtUtc { get; init; }

    public DateTime ConfirmedDownAtUtc { get; init; }

    public DateTime? EndedAtUtc { get; init; }

    public long DowntimeSeconds { get; init; }

    public long DetectionDelaySeconds { get; init; }

    public int FailureCount { get; init; }

    public string ErrorCode { get; init; } = string.Empty;

    public string ErrorMessage { get; init; } = string.Empty;

    public string NotificationStatus { get; init; } = string.Empty;

    public bool MaintenanceRelated { get; init; }
}

public sealed class AvailabilityDashboardSummary
{
    public int TotalActiveDevices { get; init; }

    public int Up { get; init; }

    public int Down { get; init; }

    public int Unknown { get; init; }

    public int Maintenance { get; init; }

    public int OpenIncidentCount { get; init; }

    public double? Availability24HoursPercent { get; init; }

    public double? Availability7DaysPercent { get; init; }

    public double? Availability30DaysPercent { get; init; }

    public double? CoveragePercent { get; init; }

    public int SlaViolationDeviceCount { get; init; }
}

public sealed class AvailabilityTimelineItem
{
    public string ItemType { get; init; } = string.Empty;

    public AvailabilityStatus? Status { get; init; }

    public DateTime StartedAtUtc { get; init; }

    public DateTime? EndedAtUtc { get; init; }

    public string Title { get; init; } = string.Empty;

    public string Detail { get; init; } = string.Empty;
}
