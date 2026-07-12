namespace NetworkHealthMonitor.Services;

public sealed class SchedulerRuntimeOptions
{
    public TimeSpan? PollIntervalOverride { get; init; }
}
