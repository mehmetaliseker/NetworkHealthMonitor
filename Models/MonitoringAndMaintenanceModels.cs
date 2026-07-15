namespace NetworkHealthMonitor.Models;

public enum MonitoringTargetType
{
    Device = 0,
    Group = 1,
    AllDevices = 2
}

public enum MaintenanceWindowStatus
{
    Scheduled = 0,
    Active = 1,
    Completed = 2,
    Cancelled = 3
}

public sealed class MonitoringCalendar
{
    public long Id { get; init; }

    public string Name { get; init; } = string.Empty;

    public string TimezoneId { get; init; } = TimeZoneInfo.Local.Id;

    public bool IsDefault { get; init; }

    public DateTime CreatedAtUtc { get; init; }

    public DateTime UpdatedAtUtc { get; init; }
}

public sealed class MonitoringCalendarRule
{
    public long Id { get; init; }

    public long CalendarId { get; init; }

    public DayOfWeek DayOfWeek { get; init; }

    public TimeSpan StartTime { get; init; }

    public TimeSpan EndTime { get; init; }

    public bool IsEnabled { get; init; } = true;
}

public sealed class DeviceMonitoringCalendarAssignment
{
    public long Id { get; init; }

    public MonitoringTargetType TargetType { get; init; }

    public int? TargetId { get; init; }

    public long CalendarId { get; init; }

    public DateTime CreatedAtUtc { get; init; }
}

public sealed class MaintenanceWindow
{
    public long Id { get; init; }

    public string Name { get; init; } = string.Empty;

    public DateTime StartedAtUtc { get; init; }

    public DateTime EndedAtUtc { get; init; }

    public string Reason { get; init; } = string.Empty;

    public bool SuppressNotifications { get; init; } = true;

    public bool ContinuePings { get; init; } = true;

    public MaintenanceWindowStatus Status { get; init; } = MaintenanceWindowStatus.Scheduled;

    public string CreatedBy { get; init; } = string.Empty;

    public DateTime CreatedAtUtc { get; init; }

    public DateTime UpdatedAtUtc { get; init; }
}

public sealed class MaintenanceWindowTarget
{
    public long Id { get; init; }

    public long MaintenanceWindowId { get; init; }

    public MonitoringTargetType TargetType { get; init; }

    public int? TargetId { get; init; }
}

public sealed class MaintenanceWindowListItem
{
    public MaintenanceWindow Window { get; init; } = new();

    public IReadOnlyList<MaintenanceWindowTarget> Targets { get; init; } = Array.Empty<MaintenanceWindowTarget>();

    public string Name => Window.Name;

    public string StatusText => Window.Status.ToString();

    public string StartedAtText => Window.StartedAtUtc.ToLocalTime().ToString("dd.MM.yyyy HH:mm:ss");

    public string EndedAtText => Window.EndedAtUtc.ToLocalTime().ToString("dd.MM.yyyy HH:mm:ss");

    public string TargetText => Targets.Count == 0
        ? "Tum cihazlar"
        : string.Join(", ", Targets.Select(target => target.TargetType switch
        {
            MonitoringTargetType.Device => $"Cihaz #{target.TargetId}",
            MonitoringTargetType.Group => $"Grup #{target.TargetId}",
            _ => "Tum cihazlar"
        }));

    public string SuppressNotificationsText => Window.SuppressNotifications ? "Evet" : "Hayir";

    public string ContinuePingsText => Window.ContinuePings ? "Evet" : "Hayir";
}

public sealed class MonitoringCalendarListItem
{
    public MonitoringCalendar Calendar { get; init; } = new();

    public IReadOnlyList<MonitoringCalendarRule> Rules { get; init; } = Array.Empty<MonitoringCalendarRule>();

    public IReadOnlyList<DeviceMonitoringCalendarAssignment> Assignments { get; init; } = Array.Empty<DeviceMonitoringCalendarAssignment>();

    public string Name => Calendar.Name;

    public string TimezoneId => Calendar.TimezoneId;

    public string IsDefaultText => Calendar.IsDefault ? "Evet" : "Hayir";

    public string RulesText => Rules.Count == 0
        ? "24x7"
        : string.Join("; ", Rules
            .Where(rule => rule.IsEnabled)
            .OrderBy(rule => rule.DayOfWeek)
            .Select(rule => $"{rule.DayOfWeek} {rule.StartTime:hh\\:mm}-{rule.EndTime:hh\\:mm}"));

    public string AssignmentsText => Assignments.Count == 0
        ? "Atama yok"
        : string.Join(", ", Assignments.Select(assignment => assignment.TargetType switch
        {
            MonitoringTargetType.Device => $"Cihaz #{assignment.TargetId}",
            MonitoringTargetType.Group => $"Grup #{assignment.TargetId}",
            _ => "Tum cihazlar"
        }));
}
