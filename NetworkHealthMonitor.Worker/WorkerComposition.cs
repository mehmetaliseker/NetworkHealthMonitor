using NetworkHealthMonitor.Data;
using NetworkHealthMonitor.Services;

namespace NetworkHealthMonitor.Worker;

public static class WorkerComposition
{
    public static async Task<ISchedulerService> CreateSchedulerAsync(WorkerOptions options)
    {
        DatabasePaths.Configure(options.PathProvider, options.LegacyDataDirectory);

        var connectionFactory = new SqliteConnectionFactory();
        await connectionFactory.InitializeAsync();

        var deviceRepository = new DeviceRepository(connectionFactory);
        var deviceGroupRepository = new DeviceGroupRepository(connectionFactory);
        var pingLogRepository = new PingLogRepository(connectionFactory);
        var schedulePlanRepository = new SchedulePlanRepository(connectionFactory);
        var outageRepository = new OutageRepository(connectionFactory);
        var settingsService = new AppSettingsService();
        var deviceCheckPolicyService = new DeviceCheckPolicyService();
        var pingExecutionService = new PingExecutionService(
            deviceRepository,
            deviceGroupRepository,
            pingLogRepository,
            outageRepository,
            new PingService(),
            deviceCheckPolicyService,
            new DeviceHealthEvaluator(),
            settingsService);

        return new SchedulerService(
            deviceRepository,
            deviceGroupRepository,
            schedulePlanRepository,
            pingLogRepository,
            pingExecutionService,
            new SchedulePlanTargetResolver(),
            deviceCheckPolicyService,
            settingsService,
            new ScheduleTimingService(),
            new SystemClock(),
            new SchedulerRuntimeOptions { PollIntervalOverride = options.PollIntervalOverride });
    }
}
