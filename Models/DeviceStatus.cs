namespace NetworkHealthMonitor.Models;

public enum DeviceStatus
{
    NotChecked,
    Checking,
    Reachable,
    Unreachable
}

public static class DeviceStatusExtensions
{
    public static string ToDisplayName(this DeviceStatus status)
    {
        return status switch
        {
            DeviceStatus.Reachable => "Erişilebilir",
            DeviceStatus.Unreachable => "Erişilemiyor",
            DeviceStatus.Checking => "Kontrol ediliyor",
            DeviceStatus.NotChecked => "Kontrol edilmedi",
            _ => "Kontrol edilmedi"
        };
    }

    public static string ToStorageValue(this DeviceStatus status)
    {
        return status == DeviceStatus.Checking ? DeviceStatus.NotChecked.ToString() : status.ToString();
    }

    public static DeviceStatus FromStorageValue(string? value)
    {
        if (Enum.TryParse<DeviceStatus>(value, true, out var parsed))
        {
            return parsed == DeviceStatus.Checking ? DeviceStatus.NotChecked : parsed;
        }

        return value?.Trim().ToLowerInvariant() switch
        {
            "erişilebilir" or "erisilebilir" => DeviceStatus.Reachable,
            "erişilemiyor" or "erisilemiyor" => DeviceStatus.Unreachable,
            "kontrol ediliyor" => DeviceStatus.Checking,
            _ => DeviceStatus.NotChecked
        };
    }
}
