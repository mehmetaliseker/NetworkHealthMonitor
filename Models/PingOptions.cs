namespace NetworkHealthMonitor.Models;

public sealed record PingOptions(int TimeoutMs, int MaxParallelPings, int FailureThreshold = AppSettings.DefaultFailureThresholdValue)
{
    public int TimeoutMs { get; init; } = Math.Clamp(TimeoutMs, AppSettings.MinPingTimeoutMs, AppSettings.MaxPingTimeoutMs);

    public int MaxParallelPings { get; init; } = Math.Clamp(MaxParallelPings, AppSettings.MinParallelPings, AppSettings.MaxParallelPingsLimit);

    public int FailureThreshold { get; init; } = Math.Clamp(FailureThreshold, AppSettings.MinFailureThreshold, AppSettings.MaxFailureThreshold);
}
