using NetworkHealthMonitor.Models;

namespace NetworkHealthMonitor.Services;

public interface IDeviceHealthEvaluator
{
    DeviceStatus Evaluate(Device device, PingDeviceResult result, DeviceCheckPolicy policy);
}
