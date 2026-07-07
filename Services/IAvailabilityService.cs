using NetworkHealthMonitor.Models;

namespace NetworkHealthMonitor.Services;

public interface IAvailabilityService
{
    Task<IReadOnlyList<AvailabilityReportItem>> GetDeviceAvailabilityAsync(DateTime since);
}
