using NetworkHealthMonitor.Data;
using NetworkHealthMonitor.Models;
using NetworkHealthMonitor.Services;
using System.Reflection;

namespace NetworkHealthMonitor.Worker;

public static class WorkerComposition
{
    public static async Task<WorkerRuntime> CreateRuntimeAsync(WorkerOptions options)
    {
        DatabasePaths.Configure(options.PathProvider, options.LegacyDataDirectory);

        var connectionFactory = new SqliteConnectionFactory();
        await connectionFactory.InitializeAsync();

        var workerInstanceId = $"{Environment.MachineName}-{Environment.ProcessId}-{Guid.NewGuid():N}";
        var settingsService = new AppSettingsService();
        var settings = await settingsService.LoadAsync();
        var heartbeatRepository = new WorkerHeartbeatRepository(connectionFactory);
        var previousHeartbeat = await heartbeatRepository.GetLatestAsync();
        var availabilityRepository = new AvailabilityRepository(connectionFactory);
        var startedAtUtc = DateTime.UtcNow;
        await heartbeatRepository.UpsertStartedAsync(new WorkerHeartbeatSnapshot
        {
            WorkerInstanceId = workerInstanceId,
            MachineName = Environment.MachineName,
            ProcessId = Environment.ProcessId,
            Version = Assembly.GetExecutingAssembly().GetName().Version?.ToString() ?? "unknown",
            StartedAtUtc = startedAtUtc,
            LastSeenAtUtc = startedAtUtc,
            Status = "Running"
        });
        await availabilityRepository.ReconcileWorkerHeartbeatGapAsync(
            previousHeartbeat?.LastSeenAtUtc,
            startedAtUtc,
            settings.HeartbeatGraceSeconds);

        var outboxRepository = new NotificationOutboxRepository(connectionFactory);
        var alertPolicyService = new AlertPolicyService();
        var dispatcher = new NotificationDispatcherService(
            outboxRepository,
            new NtfyNotificationClient(new DefaultHttpClientFactory(), new DpapiSecretProtector()),
            settingsService,
            alertPolicyService,
            workerInstanceId,
            heartbeatRepository);

        var scheduler = await CreateSchedulerAsync(options, connectionFactory, settingsService, heartbeatRepository, availabilityRepository, workerInstanceId);
        return new WorkerRuntime(workerInstanceId, scheduler, dispatcher, heartbeatRepository);
    }

    public static async Task<ISchedulerService> CreateSchedulerAsync(WorkerOptions options)
    {
        DatabasePaths.Configure(options.PathProvider, options.LegacyDataDirectory);

        var connectionFactory = new SqliteConnectionFactory();
        await connectionFactory.InitializeAsync();
        return await CreateSchedulerAsync(options, connectionFactory, new AppSettingsService(), null, new AvailabilityRepository(connectionFactory), null);
    }

    private static Task<ISchedulerService> CreateSchedulerAsync(
        WorkerOptions options,
        SqliteConnectionFactory connectionFactory,
        AppSettingsService settingsService,
        WorkerHeartbeatRepository? heartbeatRepository,
        AvailabilityRepository? availabilityRepository,
        string? workerInstanceId)
    {
        var deviceRepository = new DeviceRepository(connectionFactory);
        var deviceGroupRepository = new DeviceGroupRepository(connectionFactory);
        var pingLogRepository = new PingLogRepository(connectionFactory);
        var schedulePlanRepository = new SchedulePlanRepository(connectionFactory);
        var outageRepository = new OutageRepository(connectionFactory);
        var deviceCheckPolicyService = new DeviceCheckPolicyService();
        var alertPolicyService = new AlertPolicyService();
        var incidentService = new IncidentService(connectionFactory, settingsService, alertPolicyService);
        var pingExecutionService = new PingExecutionService(
            deviceRepository,
            deviceGroupRepository,
            pingLogRepository,
            outageRepository,
            new PingService(),
            deviceCheckPolicyService,
            new DeviceHealthEvaluator(),
            settingsService,
            incidentService,
            heartbeatRepository,
            availabilityRepository,
            workerInstanceId);

        return Task.FromResult<ISchedulerService>(new SchedulerService(
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
            new SchedulerRuntimeOptions { PollIntervalOverride = options.PollIntervalOverride },
            heartbeatRepository,
            availabilityRepository,
            workerInstanceId));
    }
}
