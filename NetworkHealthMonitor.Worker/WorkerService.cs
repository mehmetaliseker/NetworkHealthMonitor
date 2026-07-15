using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using NetworkHealthMonitor.Infrastructure;
using NetworkHealthMonitor.Services;

namespace NetworkHealthMonitor.Worker;

public sealed class WorkerService : BackgroundService
{
    private readonly WorkerOptions _options;
    private readonly ILogger<WorkerService> _logger;
    private WorkerRuntime? _runtime;

    public WorkerService(WorkerOptions options, ILogger<WorkerService> logger)
    {
        _options = options;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            _runtime = await WorkerComposition.CreateRuntimeAsync(_options);
            _runtime.Scheduler.StatusChanged += SchedulerStatusChanged;
            await _runtime.Scheduler.StartAsync(stoppingToken);

            var heartbeatTask = RunHeartbeatAsync(_runtime, stoppingToken);
            var dispatcherTask = _runtime.NotificationDispatcher.RunAsync(stoppingToken);
            await Task.Delay(Timeout.InfiniteTimeSpan, stoppingToken);
            await Task.WhenAll(heartbeatTask, dispatcherTask);
        }
        catch (OperationCanceledException)
        {
            // Expected on service stop.
        }
        catch (Exception ex)
        {
            AppErrorLogger.Log(ex, "WorkerService");
            _logger.LogError(ex, "Worker service failed.");
            throw;
        }
        finally
        {
            if (_runtime is not null)
            {
                _runtime.Scheduler.StatusChanged -= SchedulerStatusChanged;
                await _runtime.Scheduler.DisposeAsync();
            }
        }
    }

    private static async Task RunHeartbeatAsync(WorkerRuntime runtime, CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            await runtime.HeartbeatRepository.TouchAsync(runtime.WorkerInstanceId, "Running", cancellationToken: stoppingToken);
            await Task.Delay(TimeSpan.FromSeconds(15), stoppingToken);
        }
    }

    private void SchedulerStatusChanged(object? sender, SchedulerStatusChangedEventArgs e)
    {
        _logger.LogInformation("{Message}", e.Message);
        AppErrorLogger.LogInfo(e.Message);
    }
}
