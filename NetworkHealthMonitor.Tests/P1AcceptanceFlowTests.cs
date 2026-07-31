using Microsoft.Data.Sqlite;
using NetworkHealthMonitor.Data;
using NetworkHealthMonitor.Models;
using NetworkHealthMonitor.Services;
using Xunit;

namespace NetworkHealthMonitor.Tests;

public sealed class P1AcceptanceFlowTests
{
    [Fact]
    public async Task Single_ping_writes_log_updates_device_and_rejects_inactive_device()
    {
        await using var store = await TestStore.CreateAsync();
        var ping = new FakePingService(true);
        var services = CreateServices(store, ping);
        var active = await SaveDeviceAsync(services.DeviceService, "NHR-ACCEPTANCE-ONLINE", "acceptance-online.local");
        var inactive = await SaveDeviceAsync(services.DeviceService, "NHR-ACCEPTANCE-INACTIVE", "inactive-acceptance.local", isActive: false);

        var result = await services.PingExecution.PingDevicesAsync(
            new[] { active, inactive },
            new PingOptions(1000, 2, 1),
            PingTriggerType.Manual);

        Assert.Single(result.Results);
        Assert.Equal(1, result.SuccessCount);
        Assert.Equal(1, ping.PingCount);
        Assert.DoesNotContain(inactive.Id, ping.PingedDeviceIds);

        var refreshed = await services.DeviceRepository.GetByIdAsync(active.Id);
        Assert.NotNull(refreshed);
        Assert.Equal(DeviceStatus.Online, refreshed.LastStatus);
        Assert.Equal(1, refreshed.LastLatencyMs);
        Assert.Equal(1, await ScalarIntAsync(store, "SELECT COUNT(*) FROM PingLogs WHERE DeviceId = @DeviceId;", ("@DeviceId", active.Id)));
    }

    [Fact]
    public async Task Bulk_ping_deduplicates_targets_reports_progress_and_skips_disabled_devices()
    {
        await using var store = await TestStore.CreateAsync();
        var ping = new FakePingService(true, false);
        var services = CreateServices(store, ping);
        var first = await SaveDeviceAsync(services.DeviceService, "NHR-ACCEPTANCE-BULK-1", "bulk-one.local");
        var second = await SaveDeviceAsync(services.DeviceService, "NHR-ACCEPTANCE-BULK-2", "bulk-two.local");
        var disabled = await SaveDeviceAsync(services.DeviceService, "NHR-ACCEPTANCE-BULK-DISABLED", "bulk-disabled.local", isEnabled: false);
        var progressItems = new List<PingProgress>();

        var result = await services.PingExecution.PingDevicesAsync(
            new[] { first, second, first, disabled },
            new PingOptions(1000, 2, 1),
            PingTriggerType.Manual,
            progress: new InlineProgress<PingProgress>(progressItems.Add));

        Assert.Equal(2, result.Results.Count);
        Assert.Equal(2, ping.PingCount);
        Assert.Equal(new[] { first.Id, second.Id }, ping.PingedDeviceIds);
        Assert.DoesNotContain(disabled.Id, ping.PingedDeviceIds);
        var finalProgress = Assert.Single(progressItems, item => item.Completed == 2);
        Assert.Equal(2, finalProgress.Total);
        Assert.Equal(1, finalProgress.Success);
        Assert.Equal(1, finalProgress.Failure);
        Assert.Equal(2, await ScalarIntAsync(store, "SELECT COUNT(*) FROM PingLogs WHERE DeviceId IN (@FirstId, @SecondId);", ("@FirstId", first.Id), ("@SecondId", second.Id)));
    }

    [Fact]
    public async Task Scheduler_runs_due_plan_once_continues_after_restart_and_ignores_inactive_plan()
    {
        await using var store = await TestStore.CreateAsync();
        var clock = new MutableClock(new DateTime(2026, 7, 31, 10, 0, 0, DateTimeKind.Utc));
        var firstRun = CreateServices(store, new FakePingService(() => clock.Now, true), clock);
        var device = await SaveDeviceAsync(firstRun.DeviceService, "NHR-ACCEPTANCE-SCHEDULED", "scheduled.local");
        var plan = new SchedulePlan
        {
            Name = "NHR-ACCEPTANCE-P1-SCHEDULE",
            TargetType = SchedulePlanTargetType.Device,
            TargetValue = device.Id.ToString(),
            ScheduleMode = ScheduleMode.FixedInterval,
            IntervalValue = 1,
            IntervalUnit = ScheduleIntervalUnit.Minutes,
            IntervalMinutes = 1,
            TimeoutMs = 1000,
            MaxParallelism = 1,
            FailureThreshold = 1,
            IsActive = true,
            NextRunAt = clock.UtcNow.AddSeconds(-1)
        };
        Assert.True((await firstRun.SchedulePlanService.SaveAsync(plan)).Success);

        await firstRun.Scheduler.RunDuePlansOnceAsync();
        Assert.Equal(1, await CountPlanLogsAsync(store, plan.Id));
        var afterFirstRun = Assert.Single(await firstRun.SchedulePlanRepository.GetAllAsync(), item => item.Id == plan.Id);
        Assert.NotNull(afterFirstRun.LastRunAt);
        Assert.NotNull(afterFirstRun.NextRunAt);
        Assert.True(afterFirstRun.NextRunAt > afterFirstRun.LastRunAt);

        await firstRun.Scheduler.RunDuePlansOnceAsync();
        Assert.Equal(1, await CountPlanLogsAsync(store, plan.Id));

        var inactivePlan = new SchedulePlan
        {
            Name = "NHR-ACCEPTANCE-P1-INACTIVE-SCHEDULE",
            TargetType = SchedulePlanTargetType.Device,
            TargetValue = device.Id.ToString(),
            ScheduleMode = ScheduleMode.FixedInterval,
            IntervalValue = 1,
            IntervalUnit = ScheduleIntervalUnit.Minutes,
            IntervalMinutes = 1,
            TimeoutMs = 1000,
            MaxParallelism = 1,
            FailureThreshold = 1,
            IsActive = false,
            NextRunAt = clock.UtcNow.AddMinutes(-5)
        };
        Assert.True((await firstRun.SchedulePlanService.SaveAsync(inactivePlan)).Success);
        await firstRun.Scheduler.RunDuePlansOnceAsync();
        Assert.Equal(0, await CountPlanLogsAsync(store, inactivePlan.Id));

        clock.Advance(TimeSpan.FromMinutes(2));
        var restartedWorker = CreateServices(store, new FakePingService(() => clock.Now, true), clock);
        await restartedWorker.Scheduler.RunDuePlansOnceAsync();

        Assert.Equal(2, await CountPlanLogsAsync(store, plan.Id));
    }

    [Fact]
    public async Task Retry_failure_opens_single_incident_queues_notifications_and_success_closes_it()
    {
        await using var store = await TestStore.CreateAsync();
        var clock = new MutableClock(new DateTime(2026, 7, 31, 11, 0, 0, DateTimeKind.Utc));
        await SaveSettingsAsync(settings =>
        {
            settings.DefaultFailureThreshold = 1;
            settings.DefaultFailureRetryLimit = 1;
            settings.DefaultFailureRetryIntervalSeconds = 10;
            settings.Notifications.Enabled = true;
            settings.Notifications.Topic = "nhm-p1-tests";
            settings.Notifications.NotifyOnDeviceDown = true;
            settings.Notifications.NotifyOnDeviceRecovered = true;
            settings.Notifications.RecoverySuccessThreshold = 1;
            settings.Notifications.EmailEnabled = true;
            settings.Notifications.EmailNotifyOnDeviceRecovered = true;
            settings.Notifications.SmtpHost = "localhost";
            settings.Notifications.SenderEmail = "nhm@example.invalid";
            settings.Notifications.InitialEmailRecipients.Add(new EmailRecipient { Email = "ops@example.invalid" });
        });

        var ping = new FakePingService(() => clock.Now, true, false, false, false, true);
        var services = CreateServices(store, ping, clock);
        var device = await SaveDeviceAsync(services.DeviceService, "NHR-ACCEPTANCE-INCIDENT", "incident.local");

        await services.PingExecution.PingDevicesAsync(new[] { device }, new PingOptions(1000, 1, 1), PingTriggerType.Manual);
        clock.Advance(TimeSpan.FromSeconds(1));
        await services.PingExecution.PingDevicesAsync(new[] { device }, new PingOptions(1000, 1, 1), PingTriggerType.Manual);
        var afterFirstFailure = await services.DeviceRepository.GetByIdAsync(device.Id);
        Assert.NotNull(afterFirstFailure);
        Assert.Equal(DeviceStatus.UnderWatch, afterFirstFailure.LastStatus);
        Assert.Equal(0, await CountOpenIncidentsAsync(store, device.Id));

        clock.Advance(TimeSpan.FromSeconds(11));
        await services.PingExecution.PingDevicesAsync(new[] { device }, new PingOptions(1000, 1, 1), PingTriggerType.Scheduled);
        Assert.Equal(1, await CountOpenIncidentsAsync(store, device.Id));
        Assert.Equal(DeviceStatus.Offline, (await services.DeviceRepository.GetByIdAsync(device.Id))!.LastStatus);
        var initialOutboxCount = await ScalarIntAsync(store, "SELECT COUNT(*) FROM NotificationOutbox WHERE DeviceId = @DeviceId;", ("@DeviceId", device.Id));
        Assert.Equal(2, initialOutboxCount);

        clock.Advance(TimeSpan.FromSeconds(11));
        await services.PingExecution.PingDevicesAsync(new[] { device }, new PingOptions(1000, 1, 1), PingTriggerType.Scheduled);
        Assert.Equal(1, await CountOpenIncidentsAsync(store, device.Id));
        Assert.Equal(initialOutboxCount, await ScalarIntAsync(store, "SELECT COUNT(*) FROM NotificationOutbox WHERE DeviceId = @DeviceId;", ("@DeviceId", device.Id)));

        clock.Advance(TimeSpan.FromSeconds(11));
        await services.PingExecution.PingDevicesAsync(new[] { device }, new PingOptions(1000, 1, 1), PingTriggerType.Manual);

        Assert.Equal(0, await CountOpenIncidentsAsync(store, device.Id));
        Assert.Equal(1, await ScalarIntAsync(store, "SELECT COUNT(*) FROM DeviceIncidents WHERE DeviceId = @DeviceId AND Status = 'Closed';", ("@DeviceId", device.Id)));
        Assert.Equal(4, await ScalarIntAsync(store, "SELECT COUNT(*) FROM NotificationOutbox WHERE DeviceId = @DeviceId;", ("@DeviceId", device.Id)));
        Assert.Equal(5, await ScalarIntAsync(store, "SELECT COUNT(*) FROM PingLogs WHERE DeviceId = @DeviceId;", ("@DeviceId", device.Id)));
    }

    [Fact]
    public async Task Notification_dispatcher_retries_transient_failure_and_continues_after_restart()
    {
        await using var store = await TestStore.CreateAsync();
        var clock = new MutableClock(new DateTime(2026, 7, 31, 12, 0, 0, DateTimeKind.Utc));
        await SaveSettingsAsync(settings =>
        {
            settings.Notifications.Enabled = true;
            settings.Notifications.Topic = "nhm-p1-tests";
        });

        var outbox = new NotificationOutboxRepository(store.ConnectionFactory);
        var id = await outbox.AddPendingAsync(
            new NotificationOutboxCreateRequest
            {
                EventType = NotificationEventTypes.Test,
                DeviceId = null,
                IncidentId = null,
                Channel = NotificationChannels.Ntfy,
                Recipient = "nhm-p1-tests",
                Subject = "NetworkHealthMonitor kabul testi",
                Body = "Bu bildirim gercek bir cihaz kesintisi degildir.",
                PayloadJson = "{}",
                IdempotencyKey = "p1-dispatcher-retry"
            },
            clock.UtcNow);

        var attempts = 0;
        var channel = new FakeNotificationChannel(
            NotificationChannels.Ntfy,
            _ => ++attempts == 1
                ? NotificationSendResult.TransientFailure("temporary ntfy failure")
                : NotificationSendResult.Ok(),
            initialRetryDelaySeconds: 1);
        var firstDispatcher = new NotificationDispatcherService(
            outbox,
            new[] { channel },
            new AppSettingsService(),
            new AlertPolicyService(),
            "worker-before-restart",
            clock: clock);

        await firstDispatcher.DispatchOnceAsync();
        var retryItem = Assert.Single(await outbox.GetFilteredAsync(null, null, null, null, null, 10));
        Assert.Equal(id, retryItem.Id);
        Assert.Equal(NotificationStatuses.Pending, retryItem.Status);
        Assert.Equal(1, retryItem.AttemptCount);
        Assert.Contains("temporary", retryItem.LastError, StringComparison.OrdinalIgnoreCase);

        clock.Advance(TimeSpan.FromSeconds(2));
        var restartedDispatcher = new NotificationDispatcherService(
            outbox,
            new[] { channel },
            new AppSettingsService(),
            new AlertPolicyService(),
            "worker-after-restart",
            clock: clock);

        await restartedDispatcher.DispatchOnceAsync();
        var sentItem = Assert.Single(await outbox.GetFilteredAsync(null, null, null, null, null, 10));
        Assert.Equal(NotificationStatuses.Sent, sentItem.Status);
        Assert.NotNull(sentItem.SentAtUtc);
        Assert.Equal(2, attempts);
    }

    [Fact]
    public async Task Acceptance_ping_adapter_is_disabled_by_default_and_fakes_only_when_enabled()
    {
        await using var store = await TestStore.CreateAsync();
        var settingsService = new AppSettingsService();
        var inner = new FakePingService(false);
        var service = new AcceptancePingService(inner, settingsService);
        var device = CreateDevice("NHR-ACCEPTANCE-ONLINE", "acceptance-online.local");

        var disabledResult = await service.PingAsync(device, new PingOptions(1000, 1, 1));
        Assert.False(disabledResult.IsSuccess);
        Assert.Equal(1, inner.PingCount);

        await SaveSettingsAsync(settings =>
        {
            settings.AcceptanceTest.Enabled = true;
        });

        var enabledResult = await service.PingAsync(device, new PingOptions(1000, 1, 1));
        Assert.True(enabledResult.IsSuccess);
        Assert.Equal(1, enabledResult.LatencyMs);
        Assert.Equal(1, inner.PingCount);
    }

    private static FlowServices CreateServices(TestStore store, IPingService pingService, MutableClock? clock = null)
    {
        var deviceRepository = new DeviceRepository(store.ConnectionFactory);
        var deviceGroupRepository = new DeviceGroupRepository(store.ConnectionFactory);
        var pingLogRepository = new PingLogRepository(store.ConnectionFactory);
        var schedulePlanRepository = new SchedulePlanRepository(store.ConnectionFactory);
        var outageRepository = new OutageRepository(store.ConnectionFactory);
        var settingsService = new AppSettingsService();
        var incidentService = new IncidentService(store.ConnectionFactory, settingsService, new AlertPolicyService(), clock: clock);
        var pingExecution = new PingExecutionService(
            deviceRepository,
            deviceGroupRepository,
            pingLogRepository,
            outageRepository,
            pingService,
            new DeviceCheckPolicyService(),
            new DeviceHealthEvaluator(),
            settingsService,
            incidentService,
            null,
            null,
            "p1-test-worker");
        var scheduler = new SchedulerService(
            deviceRepository,
            deviceGroupRepository,
            schedulePlanRepository,
            pingLogRepository,
            pingExecution,
            new SchedulePlanTargetResolver(),
            new DeviceCheckPolicyService(),
            settingsService,
            new ScheduleTimingService(),
            clock,
            new SchedulerRuntimeOptions { PollIntervalOverride = TimeSpan.FromSeconds(1) },
            incidentService: incidentService,
            workerInstanceId: "p1-test-worker");

        return new FlowServices(
            deviceRepository,
            schedulePlanRepository,
            new DeviceService(deviceRepository),
            new SchedulePlanService(schedulePlanRepository),
            pingExecution,
            scheduler);
    }

    private static Device CreateDevice(
        string name,
        string address,
        bool isActive = true,
        bool isEnabled = true)
    {
        return new Device
        {
            Name = name,
            IpAddress = address,
            DeviceType = DeviceType.Server,
            IsActive = isActive,
            IsEnabled = isEnabled,
            AutoCheckEnabled = true,
            GroupName = "NHR-ACCEPTANCE",
            Description = "P1 acceptance flow test"
        };
    }

    private static async Task<Device> SaveDeviceAsync(
        DeviceService service,
        string name,
        string address,
        bool isActive = true,
        bool isEnabled = true)
    {
        var device = CreateDevice(name, address, isActive, isEnabled);
        var result = await service.SaveAsync(device);
        Assert.True(result.Success, result.Message);
        return device;
    }

    private static async Task SaveSettingsAsync(Action<AppSettings> configure)
    {
        var settingsService = new AppSettingsService();
        var settings = await settingsService.LoadAsync();
        configure(settings);
        await settingsService.SaveAsync(settings);
    }

    private static Task<int> CountPlanLogsAsync(TestStore store, int planId)
    {
        return ScalarIntAsync(store, "SELECT COUNT(*) FROM PingLogs WHERE SchedulePlanId = @PlanId;", ("@PlanId", planId));
    }

    private static Task<int> CountOpenIncidentsAsync(TestStore store, int deviceId)
    {
        return ScalarIntAsync(store, "SELECT COUNT(*) FROM DeviceIncidents WHERE DeviceId = @DeviceId AND Status = 'Open';", ("@DeviceId", deviceId));
    }

    private static async Task<int> ScalarIntAsync(TestStore store, string sql, params (string Name, object Value)[] parameters)
    {
        await using var connection = await store.ConnectionFactory.CreateOpenConnectionAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        foreach (var parameter in parameters)
        {
            command.Parameters.AddWithValue(parameter.Name, parameter.Value);
        }

        return Convert.ToInt32(await command.ExecuteScalarAsync());
    }

    private sealed record FlowServices(
        DeviceRepository DeviceRepository,
        SchedulePlanRepository SchedulePlanRepository,
        DeviceService DeviceService,
        SchedulePlanService SchedulePlanService,
        PingExecutionService PingExecution,
        SchedulerService Scheduler);

    private sealed class MutableClock : ISystemClock, IClock
    {
        public MutableClock(DateTime utcNow)
        {
            UtcNow = DateTime.SpecifyKind(utcNow, DateTimeKind.Utc);
        }

        public DateTime Now => UtcNow;

        public DateTime UtcNow { get; private set; }

        public void Advance(TimeSpan value)
        {
            UtcNow = UtcNow.Add(value);
        }
    }

    private sealed class InlineProgress<T> : IProgress<T>
    {
        private readonly Action<T> _handler;

        public InlineProgress(Action<T> handler)
        {
            _handler = handler;
        }

        public void Report(T value)
        {
            _handler(value);
        }
    }
}
