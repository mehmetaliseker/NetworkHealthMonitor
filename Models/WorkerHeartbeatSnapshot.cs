namespace NetworkHealthMonitor.Models;

public sealed class WorkerHeartbeatSnapshot
{
    public string WorkerInstanceId { get; set; } = string.Empty;

    public string MachineName { get; set; } = string.Empty;

    public int ProcessId { get; set; }

    public string Version { get; set; } = string.Empty;

    public DateTime StartedAtUtc { get; set; }

    public DateTime LastSeenAtUtc { get; set; }

    public DateTime? LastSchedulerCycleAtUtc { get; set; }

    public DateTime? LastSchedulerPollAtUtc { get; set; }

    public DateTime? LastSuccessfulPingAtUtc { get; set; }

    public DateTime? LastNotificationDispatchAtUtc { get; set; }

    public string Status { get; set; } = string.Empty;

    public string LastError { get; set; } = string.Empty;

    public string LastCriticalError { get; set; } = string.Empty;

    public string LastDatabaseLockedError { get; set; } = string.Empty;

    public string LastSchedulerException { get; set; } = string.Empty;

    public string LastNtfyException { get; set; } = string.Empty;

    public double AverageSchedulerCycleMs { get; set; }
}
