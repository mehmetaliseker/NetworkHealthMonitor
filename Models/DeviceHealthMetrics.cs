namespace NetworkHealthMonitor.Models;

public sealed class DeviceHealthMetrics
{
    public int DeviceId { get; init; }

    public double? Uptime24HoursPercent { get; init; }

    public double? Uptime7DaysPercent { get; init; }

    public double? Uptime30DaysPercent { get; init; }

    public long? AverageLatencyMs { get; init; }

    public DateTime? LastFailureAt { get; init; }

    public int FailureCount30Days { get; init; }

    public int TotalChecks30Days { get; init; }
}
