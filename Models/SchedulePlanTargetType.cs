namespace NetworkHealthMonitor.Models;

public enum SchedulePlanTargetType
{
    Device,
    DeviceType,
    DeviceGroup,
    CriticalDevices,
    AllDevices
}

public static class SchedulePlanTargetTypeExtensions
{
    public static string ToDisplayName(this SchedulePlanTargetType targetType)
    {
        return targetType switch
        {
            SchedulePlanTargetType.Device => "Tek cihaz",
            SchedulePlanTargetType.DeviceType => "Cihaz tipi",
            SchedulePlanTargetType.DeviceGroup => "Cihaz grubu",
            SchedulePlanTargetType.CriticalDevices => "Kritik cihazlar",
            SchedulePlanTargetType.AllDevices => "Tüm cihazlar",
            _ => "Tüm cihazlar"
        };
    }

    public static string ToStorageValue(this SchedulePlanTargetType targetType)
    {
        return targetType.ToString();
    }

    public static SchedulePlanTargetType FromStorageValue(string? value)
    {
        return Enum.TryParse<SchedulePlanTargetType>(value, true, out var parsed)
            ? parsed
            : SchedulePlanTargetType.AllDevices;
    }
}
