using NetworkHealthMonitor.Models;

namespace NetworkHealthMonitor.Services;

public interface IDeviceCheckPolicyService
{
    DeviceCheckPolicy ResolvePolicy(
        Device device,
        DeviceGroup? group,
        SchedulePlan? schedulePlan,
        AppSettings settings,
        PingOptions? options = null);

    bool IsDue(Device device, DeviceCheckPolicy policy, DateTime now);

    DateTime? GetNextCheckAt(Device device, DeviceCheckPolicy policy);
}
