namespace NetworkHealthMonitor.Models;

public enum DeviceType
{
    Camera,
    AccessPoint,
    Computer,
    Switch,
    Server,
    Printer,
    Other
}

public static class DeviceTypeExtensions
{
    public static string ToDisplayName(this DeviceType deviceType)
    {
        return deviceType switch
        {
            DeviceType.Camera => "Kamera",
            DeviceType.AccessPoint => "Access Point",
            DeviceType.Computer => "Bilgisayar",
            DeviceType.Switch => "Switch",
            DeviceType.Server => "Sunucu",
            DeviceType.Printer => "Yazıcı",
            DeviceType.Other => "Diğer",
            _ => "Diğer"
        };
    }

    public static string ToStorageValue(this DeviceType deviceType)
    {
        return deviceType.ToString();
    }

    public static DeviceType FromStorageValue(string? value)
    {
        return TryParse(value, out var parsed) ? parsed : DeviceType.Other;
    }

    public static bool TryParse(string? value, out DeviceType deviceType)
    {
        if (Enum.TryParse<DeviceType>(value, true, out deviceType))
        {
            return true;
        }

        return value?.Trim().ToLowerInvariant() switch
        {
            "kamera" => Set(DeviceType.Camera, out deviceType),
            "access point" or "ap" => Set(DeviceType.AccessPoint, out deviceType),
            "bilgisayar" or "pc" => Set(DeviceType.Computer, out deviceType),
            "switch" => Set(DeviceType.Switch, out deviceType),
            "sunucu" or "server" => Set(DeviceType.Server, out deviceType),
            "yazıcı" or "yazici" or "printer" => Set(DeviceType.Printer, out deviceType),
            "diğer" or "diger" => Set(DeviceType.Other, out deviceType),
            _ => false
        };
    }

    private static bool Set(DeviceType value, out DeviceType deviceType)
    {
        deviceType = value;
        return true;
    }
}
