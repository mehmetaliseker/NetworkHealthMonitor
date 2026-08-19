namespace NetworkHealthMonitor.Tray.Services;

public sealed record WorkerServiceStatus(
    WorkerServiceState State,
    string DisplayText,
    string TechnicalDetail = "")
{
    public bool IsInstalled => State != WorkerServiceState.NotInstalled;

    public bool IsRunning => State == WorkerServiceState.Running;

    public bool IsStopped => State == WorkerServiceState.Stopped;

    public bool IsTransitioning => State is WorkerServiceState.StartPending or WorkerServiceState.StopPending;

    public bool IsError => State == WorkerServiceState.Error;

    public static WorkerServiceStatus NotInstalled(string detail = "")
    {
        return new WorkerServiceStatus(WorkerServiceState.NotInstalled, "Worker kurulu değil", detail);
    }

    public static WorkerServiceStatus Error(string displayText, string detail = "")
    {
        return new WorkerServiceStatus(WorkerServiceState.Error, displayText, detail);
    }
}
