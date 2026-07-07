using NetworkHealthMonitor.Models;

namespace NetworkHealthMonitor.Services;

public interface IDeviceGroupService
{
    Task<OperationResult> SaveAsync(DeviceGroup group);

    Task<OperationResult> DeleteAsync(DeviceGroup group);
}
