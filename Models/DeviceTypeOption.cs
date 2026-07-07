namespace NetworkHealthMonitor.Models;

public sealed class DeviceTypeOption
{
    public DeviceTypeOption(DeviceType value)
    {
        Value = value;
        Label = value.ToDisplayName();
    }

    public DeviceType Value { get; }

    public string Label { get; }

    public static IReadOnlyList<DeviceTypeOption> CreateAll()
    {
        return Enum.GetValues<DeviceType>().Select(type => new DeviceTypeOption(type)).ToList();
    }
}
