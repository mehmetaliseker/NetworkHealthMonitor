using NetworkHealthMonitor.Models;

namespace NetworkHealthMonitor.ViewModels;

public sealed class DeviceGroupSummaryViewModel
{
    public DeviceGroup? Group { get; init; }

    public string Name { get; init; } = string.Empty;

    public int DeviceCount { get; init; }

    public int OnlineCount { get; init; }

    public int OfflineCount { get; init; }

    public string DeviceCountText => DeviceCount.ToString();

    public string OnlineCountText => OnlineCount.ToString();

    public string OfflineCountText => OfflineCount.ToString();
}
