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
            PingTriggerType.Scheduled => "Otomatik",
            PingTriggerType.GroupManual => "Grup manuel",
            PingTriggerType.TypeManual => "Tip manuel",
            PingTriggerType.SelectedDeviceManual => "Seçili cihaz manuel",
            PingTriggerType.Manual => "Manuel",
            _ => "Manuel"
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
