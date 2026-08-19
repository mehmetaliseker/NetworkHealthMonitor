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

        var connectionFactory = await CreateConnectionFactoryAsync();
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

        var dispatcher = CreateNotificationDispatcher(
            connectionFactory,
            settingsService,
            workerInstanceId,
            heartbeatRepository);

        var scheduler = await CreateSchedulerAsync(options, connectionFactory, settingsService, heartbeatRepository, availabilityRepository, workerInstanceId);
        return new WorkerRuntime(workerInstanceId, scheduler, dispatcher, heartbeatRepository);
    }

    public static async Task<ISchedulerService> CreateSchedulerAsync(WorkerOptions options)
    {
        DatabasePaths.Configure(options.PathProvider, options.LegacyDataDirectory);

        var connectionFactory = await CreateConnectionFactoryAsync();
        return await CreateSchedulerAsync(options, connectionFactory, new AppSettingsService(), null, new AvailabilityRepository(connectionFactory), null);
    }

    private static async Task<SqliteConnectionFactory> CreateConnectionFactoryAsync()
    {
        var connectionFactory = new SqliteConnectionFactory();
        await connectionFactory.InitializeAsync();
        return connectionFactory;
    }

    private static NotificationDispatcherService CreateNotificationDispatcher(
        SqliteConnectionFactory connectionFactory,
        AppSettingsService settingsService,
        string workerInstanceId,
        WorkerHeartbeatRepository heartbeatRepository)
    {
        return new NotificationDispatcherService(
            new NotificationOutboxRepository(connectionFactory),
            CreateNotificationChannels(),
            settingsService,
            new AlertPolicyService(),
            workerInstanceId,
            heartbeatRepository,
            new DeviceOutageIncidentRepository(connectionFactory));
    }

    private static IReadOnlyList<INotificationChannel> CreateNotificationChannels()
    {
        return
        [
            new NtfyNotificationChannel(new NtfyNotificationClient(new DefaultHttpClientFactory(), new DpapiSecretProtector())),
            new EmailNotificationChannel(new SmtpEmailSender())
        ];
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
        var incidentService = new IncidentService(connectionFactory, settingsService, new AlertPolicyService());
        var pingExecutionService = CreatePingExecutionService(
            deviceRepository,
            deviceGroupRepository,
            pingLogRepository,
            outageRepository,
            deviceCheckPolicyService,
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
            incidentService,
            workerInstanceId));
    }

    private static PingExecutionService CreatePingExecutionService(
        DeviceRepository deviceRepository,
        DeviceGroupRepository deviceGroupRepository,
        PingLogRepository pingLogRepository,
        OutageRepository outageRepository,
        IDeviceCheckPolicyService deviceCheckPolicyService,
        AppSettingsService settingsService,
        IIncidentService incidentService,
        WorkerHeartbeatRepository? heartbeatRepository,
        AvailabilityRepository? availabilityRepository,
        string? workerInstanceId)
    {
        return new PingExecutionService(
            deviceRepository,
            deviceGroupRepository,
            pingLogRepository,
            outageRepository,
            PingServiceFactory.Create(settingsService),
            deviceCheckPolicyService,
            new DeviceHealthEvaluator(),
            settingsService,
            incidentService,
            heartbeatRepository,
            availabilityRepository,
            workerInstanceId);
    }
}
