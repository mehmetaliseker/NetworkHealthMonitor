using NetworkHealthMonitor.Models;

namespace NetworkHealthMonitor.Services;

public interface IDeviceService
{
    Task<OperationResult> SaveAsync(Device device);

    Task<OperationResult> DeleteAsync(Device device);
}
