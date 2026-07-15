using NetworkHealthMonitor.Data;
using NetworkHealthMonitor.Services;

namespace NetworkHealthMonitor.Worker;

public sealed record WorkerRuntime(
    string WorkerInstanceId,
    ISchedulerService Scheduler,
    NotificationDispatcherService NotificationDispatcher,
    WorkerHeartbeatRepository HeartbeatRepository);
