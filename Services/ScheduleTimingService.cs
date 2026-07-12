using NetworkHealthMonitor.Models;

namespace NetworkHealthMonitor.Services;

public sealed class ScheduleTimingService
{
    public bool IsDue(SchedulePlan plan, DateTime now)
    {
        return plan.IsActive && GetEffectiveNextRun(plan, now) <= now;
    }

    public DateTime GetEffectiveNextRun(SchedulePlan plan, DateTime now)
    {
        if (plan.NextRunAt.HasValue)
        {
            return plan.NextRunAt.Value;
        }

        if (plan.LastRunAt.HasValue)
        {
            return plan.LastRunAt.Value.AddMinutes(Math.Max(1, plan.IntervalMinutes));
        }

        return now;
    }

    public DateTime CalculateNextRunAfterExecution(SchedulePlan plan, DateTime executionTime)
    {
        return executionTime.AddMinutes(Math.Max(1, plan.IntervalMinutes));
    }
}
