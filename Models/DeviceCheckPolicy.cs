namespace NetworkHealthMonitor.Models;

public sealed record DeviceCheckPolicy(
    int NormalIntervalSeconds,
    int FailureRetryIntervalSeconds,
    int FailureRetryLimit,
    int FailureThreshold)
{
    public int NormalIntervalSeconds { get; init; } = Math.Clamp(NormalIntervalSeconds, AppSettings.MinDeviceCheckIntervalSeconds, AppSettings.MaxDeviceCheckIntervalSeconds);

    public int FailureRetryIntervalSeconds { get; init; } = Math.Clamp(FailureRetryIntervalSeconds, AppSettings.MinFailureRetryIntervalSeconds, AppSettings.MaxFailureRetryIntervalSeconds);

    public int FailureRetryLimit { get; init; } = Math.Clamp(FailureRetryLimit, AppSettings.MinFailureRetryLimit, AppSettings.MaxFailureRetryLimit);

    public int FailureThreshold { get; init; } = Math.Clamp(FailureThreshold, AppSettings.MinFailureThreshold, AppSettings.MaxFailureThreshold);

    public int OfflineFailureCount => FailureThreshold + FailureRetryLimit;

    public bool HasFailureRetryRemaining(int consecutiveFailureCount)
    {
        return consecutiveFailureCount > 0 && consecutiveFailureCount < OfflineFailureCount;
    }
}
