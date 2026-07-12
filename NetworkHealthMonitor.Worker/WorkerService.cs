using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using NetworkHealthMonitor.Infrastructure;
using NetworkHealthMonitor.Services;

namespace NetworkHealthMonitor.Worker;

public sealed class WorkerService : BackgroundService
{
    private readonly WorkerOptions _options;
    private readonly ILogger<WorkerService> _logger;
    private ISchedulerService? _schedulerService;

    public WorkerService(WorkerOptions options, ILogger<WorkerService> logger)
    {
        _options = options;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            _schedulerService = await WorkerComposition.CreateSchedulerAsync(_options);
            _schedulerService.StatusChanged += SchedulerStatusChanged;
            await _schedulerService.StartAsync(stoppingToken);
            await Task.Delay(Timeout.InfiniteTimeSpan, stoppingToken);
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
            if (_schedulerService is not null)
            {
                _schedulerService.StatusChanged -= SchedulerStatusChanged;
                await _schedulerService.DisposeAsync();
            }
        }
    }

    private void SchedulerStatusChanged(object? sender, SchedulerStatusChangedEventArgs e)
    {
        _logger.LogInformation("{Message}", e.Message);
        AppErrorLogger.LogInfo(e.Message);
    }
}
