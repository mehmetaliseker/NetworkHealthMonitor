using NetworkHealthMonitor.Models;

namespace NetworkHealthMonitor.Services;

public interface ISchedulePlanService
{
    Task<OperationResult> SaveAsync(SchedulePlan plan);

    Task<OperationResult> DeleteAsync(SchedulePlan plan);
}
