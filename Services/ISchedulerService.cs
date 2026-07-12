namespace NetworkHealthMonitor.Services;

public interface ISchedulerService : IAsyncDisposable
{
    event EventHandler<SchedulerStatusChangedEventArgs>? StatusChanged;

    bool IsRunning { get; }

    Task StartAsync(CancellationToken cancellationToken = default);

    Task RunDuePlansOnceAsync(CancellationToken cancellationToken = default);

    Task StopAsync();
}
