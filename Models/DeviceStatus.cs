namespace NetworkHealthMonitor.Models;

public enum DeviceStatus
{
    Unknown = 0,
    NotChecked = Unknown,
    Checking = 1,
    Online = 2,
    Reachable = Online,
    Warning = 3,
    UnderWatch = 4,
    Offline = 5,
    Unreachable = Offline,
    PingBlockedOrNoReply = 6
}

public static class DeviceStatusExtensions
{
    public static string ToDisplayName(this DeviceStatus status)
    {
        return status switch
        {
            DeviceStatus.Online => "Online / Sağlıklı",
            DeviceStatus.Warning => "Kararsız",
            DeviceStatus.UnderWatch => "Takipte",
            DeviceStatus.Offline => "Muhtemel erişilemiyor",
            DeviceStatus.PingBlockedOrNoReply => "Ping yanıtlamıyor olabilir",
            DeviceStatus.Checking => "Kontrol ediliyor",
            DeviceStatus.Unknown => "Kontrol edilmedi",
            _ => "Kontrol edilmedi"
        };
    }

    public static string ToStorageValue(this DeviceStatus status)
    {
        if (status == DeviceStatus.Checking)
        {
            return nameof(DeviceStatus.Unknown);
        }

        if (status == DeviceStatus.Online)
        {
            return nameof(DeviceStatus.Online);
        }

        if (status == DeviceStatus.Warning)
        {
            return nameof(DeviceStatus.Warning);
        }

        if (status == DeviceStatus.UnderWatch)
        {
            return nameof(DeviceStatus.UnderWatch);
        }

        if (status == DeviceStatus.Offline)
        {
            return nameof(DeviceStatus.Offline);
        }

        if (status == DeviceStatus.PingBlockedOrNoReply)
        {
            return nameof(DeviceStatus.PingBlockedOrNoReply);
        }

        return nameof(DeviceStatus.Unknown);
    }

    public static DeviceStatus FromStorageValue(string? value)
    {
        if (Enum.TryParse<DeviceStatus>(value, true, out var parsed))
        {
            return parsed == DeviceStatus.Checking ? DeviceStatus.Unknown : parsed;
        }

        return value?.Trim().ToLowerInvariant() switch
        {
            "erişilebilir" or "erisilebilir" or "online" or "sağlıklı" or "saglikli" => DeviceStatus.Online,
            "erişilemiyor" or "erisilemiyor" or "offline" => DeviceStatus.Offline,
            "kararsız" or "kararsiz" or "warning" => DeviceStatus.Warning,
            "takipte" or "underwatch" => DeviceStatus.UnderWatch,
            "ping yanıtlamıyor olabilir" or "ping yanitlamiyor olabilir" => DeviceStatus.PingBlockedOrNoReply,
            "kontrol ediliyor" => DeviceStatus.Checking,
            _ => DeviceStatus.Unknown
        };
    }

    public static bool IsSuccessful(this DeviceStatus status)
    {
        return status == DeviceStatus.Online;
    }

    public static bool IsFailureObservation(this DeviceStatus status)
    {
        return status is DeviceStatus.Warning
            or DeviceStatus.UnderWatch
            or DeviceStatus.Offline
            or DeviceStatus.PingBlockedOrNoReply;
    }

    public static bool IsProblematic(this DeviceStatus status)
    {
        return status is DeviceStatus.UnderWatch or DeviceStatus.Offline;
    }
}
