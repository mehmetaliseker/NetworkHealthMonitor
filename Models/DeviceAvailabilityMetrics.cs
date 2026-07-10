namespace NetworkHealthMonitor.Models;

public sealed class DeviceAvailabilityMetrics
{
    public int DeviceId { get; init; }

    public int TotalSuccessCount { get; init; }

    public int TotalFailureCount { get; init; }

    public double? MeasuredAvailabilityPercent { get; init; }

    public double? Availability24HoursPercent { get; init; }

    public double? Availability7DaysPercent { get; init; }

    public double? Availability30DaysPercent { get; init; }

    public double? AvailabilityOverallPercent { get; init; }

    public DateTime? LastSuccessfulCheckAt { get; init; }

    public DateTime? LastFailedCheckAt { get; init; }
}
