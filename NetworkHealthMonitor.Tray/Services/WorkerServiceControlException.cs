namespace NetworkHealthMonitor.Tray.Services;

public enum WorkerServiceControlError
{
    NotInstalled,
    AccessDenied,
    Timeout,
    Failed
}

public sealed class WorkerServiceControlException : Exception
{
    public WorkerServiceControlException(
        WorkerServiceControlError error,
        string message,
        Exception? innerException = null)
        : base(message, innerException)
    {
        Error = error;
    }

    public WorkerServiceControlError Error { get; }
}
