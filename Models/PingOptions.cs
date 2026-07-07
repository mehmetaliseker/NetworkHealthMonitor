namespace NetworkHealthMonitor.Models;

public sealed record PingOptions(int TimeoutMs, int MaxParallelPings, int FailureThreshold = 3)
{
    public int TimeoutMs { get; init; } = Math.Clamp(TimeoutMs, 250, 10000);

    public int MaxParallelPings { get; init; } = Math.Clamp(MaxParallelPings, 1, 128);

    public int FailureThreshold { get; init; } = Math.Clamp(FailureThreshold, 1, 20);
}
