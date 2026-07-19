using System.Globalization;

namespace NetworkHealthMonitor.Models;

public sealed class DeviceOutageIncident
{
    public long IncidentId { get; init; }

    public int DeviceId { get; init; }

    public string DeviceName { get; init; } = string.Empty;

    public string IpAddress { get; init; } = string.Empty;

    public DeviceType DeviceType { get; init; }

    public string GroupName { get; init; } = string.Empty;

    public DateTime StartedAtUtc { get; init; }

    public DateTime LastObservedAtUtc { get; init; }

    public DateTime? LastSuccessfulCheckAtUtc { get; init; }

    public DateTime? InitialNotificationSentAtUtc { get; init; }

    public DateTime? EscalationNotificationSentAtUtc { get; init; }

    public DateTime? ResolvedAtUtc { get; init; }

    public string CurrentState { get; init; } = "Open";

    public long SuppressedDurationSeconds { get; init; }

    public DateTime CreatedAtUtc { get; init; }

    public DateTime UpdatedAtUtc { get; init; }

    public string StartedAtText => StartedAtUtc.ToLocalTime().ToString("dd.MM.yyyy HH:mm:ss", CultureInfo.CurrentCulture);

    public string LastObservedAtText => LastObservedAtUtc.ToLocalTime().ToString("dd.MM.yyyy HH:mm:ss", CultureInfo.CurrentCulture);
}

public enum DeviceSuppressionMode
{
    None = 0,
    MuteNotifications = 1,
    PauseMonitoring = 2
}

public static class DeviceSuppressionModeExtensions
{
    public static string ToStorageValue(this DeviceSuppressionMode mode)
    {
        return mode switch
        {
            DeviceSuppressionMode.MuteNotifications => nameof(DeviceSuppressionMode.MuteNotifications),
            DeviceSuppressionMode.PauseMonitoring => nameof(DeviceSuppressionMode.PauseMonitoring),
            _ => nameof(DeviceSuppressionMode.None)
        };
    }

    public static DeviceSuppressionMode FromStorageValue(string? value)
    {
        return Enum.TryParse<DeviceSuppressionMode>(value, true, out var parsed)
            ? parsed
            : DeviceSuppressionMode.None;
    }

    public static string ToDisplayName(this DeviceSuppressionMode mode)
    {
        return mode switch
        {
            DeviceSuppressionMode.MuteNotifications => "Bildirimler Susturuldu",
            DeviceSuppressionMode.PauseMonitoring => "İzleme Duraklatıldı",
            _ => "Normal"
        };
    }
}
