namespace NetworkHealthMonitor.Models;

public sealed class AcceptanceTestSettings
{
    public const string DefaultDeviceNamePrefix = "NHR-ACCEPTANCE-";
    public const string OnlineToken = "acceptance-online";
    public const string OfflineToken = "acceptance-offline";
    public const string TimeoutToken = "acceptance-timeout";

    public bool Enabled { get; set; }

    public string DeviceNamePrefix { get; set; } = DefaultDeviceNamePrefix;
}
