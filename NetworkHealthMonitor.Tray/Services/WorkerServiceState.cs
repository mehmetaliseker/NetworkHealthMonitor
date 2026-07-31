namespace NetworkHealthMonitor.Tray.Services;

public enum WorkerServiceState
{
    NotInstalled,
    Stopped,
    Running,
    StartPending,
    StopPending,
    Paused,
    Error,
    Unknown
}
