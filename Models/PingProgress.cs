namespace NetworkHealthMonitor.Models;

public sealed record PingProgress(
    int Total,
    int Completed,
    int Success,
    int Failure,
    int? DeviceId = null,
    DeviceStatus? DeviceStatus = null,
    long? LatencyMs = null,
    DateTime? CheckedAt = null)
{
    public int Remaining => Math.Max(0, Total - Completed);
}
