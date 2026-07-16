namespace NetworkHealthMonitor.Models;

public enum PingTriggerType
{
    Manual,
    Scheduled,
    GroupManual,
    TypeManual,
    SelectedDeviceManual
}

public static class PingTriggerTypeExtensions
{
    public static string ToDisplayName(this PingTriggerType triggerType)
    {
        return triggerType switch
        {
            PingTriggerType.Scheduled => "Otomatik kontrol",
            PingTriggerType.GroupManual => "Grup manuel kontrol",
            PingTriggerType.TypeManual => "Tip manuel kontrol",
            PingTriggerType.SelectedDeviceManual => "Seçili cihaz manuel kontrol",
            PingTriggerType.Manual => "Manuel kontrol",
            _ => "Manuel kontrol"
        };
    }

    public static string ToStorageValue(this PingTriggerType triggerType)
    {
        return triggerType.ToString();
    }

    public static PingTriggerType FromStorageValue(string? value)
    {
        return Enum.TryParse<PingTriggerType>(value, true, out var parsed)
            ? parsed
            : PingTriggerType.Manual;
    }
}
