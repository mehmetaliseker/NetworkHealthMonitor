namespace NetworkHealthMonitor.Services;

public sealed class SchedulerStatusChangedEventArgs : EventArgs
{
    public SchedulerStatusChangedEventArgs(string message)
    {
        Message = message;
    }

    public string Message { get; }
}
