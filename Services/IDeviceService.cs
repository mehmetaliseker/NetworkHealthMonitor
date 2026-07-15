using NetworkHealthMonitor.Models;

namespace NetworkHealthMonitor.Services;

public interface IDeviceService
{
    Task<OperationResult> SaveAsync(Device device);

    Task<OperationResult> DeleteAsync(Device device);

    Task<OperationResult> RestoreAsync(Device device);

    Task<OperationResult> BulkDeleteAsync(IEnumerable<Device> devices);

    Task<OperationResult> BulkRestoreAsync(IEnumerable<Device> devices);

    Task<OperationResult> DeleteGroupDevicesAsync(DeviceGroup group, bool deleteEmptyGroup);
}
