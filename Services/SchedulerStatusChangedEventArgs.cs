namespace NetworkHealthMonitor.Services;

public sealed class SchedulerStatusChangedEventArgs : EventArgs
{
    public SchedulerStatusChangedEventArgs(string message, bool shouldRefresh = false)
    {
        Message = message;
        ShouldRefresh = shouldRefresh;
    }

    public string Message { get; }

    public bool ShouldRefresh { get; }
}
