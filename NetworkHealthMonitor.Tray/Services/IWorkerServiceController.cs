namespace NetworkHealthMonitor.Tray.Services;

public interface IWorkerServiceController
{
    Task<WorkerServiceStatus> GetStatusAsync(
        CancellationToken cancellationToken);

    Task StartAsync(
        CancellationToken cancellationToken);

    Task StopAsync(
        CancellationToken cancellationToken);

    Task RestartAsync(
        CancellationToken cancellationToken);
}
