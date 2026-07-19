namespace NetworkHealthMonitor.Models;

public sealed record DeviceCheckPolicy(
    bool AutoCheckEnabled,
    int NormalIntervalSeconds,
    int PingTimeoutMs,
    int FailureRetryIntervalSeconds,
    int FailureRetryLimit,
    int OfflineRecheckIntervalSeconds,
    int FailureThreshold,
    string PolicySourceText)
{
    public bool AutoCheckEnabled { get; init; } = AutoCheckEnabled;

    public int NormalIntervalSeconds { get; init; } = Math.Clamp(NormalIntervalSeconds, AppSettings.MinDeviceCheckIntervalSeconds, AppSettings.MaxDeviceCheckIntervalSeconds);

    public int PingTimeoutMs { get; init; } = Math.Clamp(PingTimeoutMs, AppSettings.MinPingTimeoutMs, AppSettings.MaxPingTimeoutMs);

    public int FailureRetryIntervalSeconds { get; init; } = Math.Clamp(FailureRetryIntervalSeconds, AppSettings.MinFailureRetryIntervalSeconds, AppSettings.MaxFailureRetryIntervalSeconds);

    public int FailureRetryLimit { get; init; } = Math.Clamp(FailureRetryLimit, AppSettings.MinFailureRetryLimit, AppSettings.MaxFailureRetryLimit);

    public int OfflineRecheckIntervalSeconds { get; init; } = Math.Clamp(OfflineRecheckIntervalSeconds, AppSettings.MinOfflineRecheckIntervalSeconds, AppSettings.MaxOfflineRecheckIntervalSeconds);

    public int FailureThreshold { get; init; } = Math.Clamp(FailureThreshold, AppSettings.MinFailureThreshold, AppSettings.MaxFailureThreshold);

    public int OfflineFailureCount => FailureThreshold + FailureRetryLimit;

    public string PolicySourceText { get; init; } = string.IsNullOrWhiteSpace(PolicySourceText) ? "Global" : PolicySourceText;

    public bool HasFailureRetryRemaining(int consecutiveFailureCount)
    {
        return consecutiveFailureCount > 0 && consecutiveFailureCount < OfflineFailureCount;
    }
}
