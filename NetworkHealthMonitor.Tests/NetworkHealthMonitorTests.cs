using Microsoft.Data.Sqlite;
using NetworkHealthMonitor.Data;
using NetworkHealthMonitor.Models;
using NetworkHealthMonitor.Services;
using NetworkHealthMonitor.Worker;
using Xunit;

namespace NetworkHealthMonitor.Tests;

public sealed class NetworkHealthMonitorTests
{
    [Fact]
    public void Ipv4_validation_accepts_and_rejects_expected_values()
    {
        Assert.True(IpAddressValidator.IsValidIpv4("192.168.1.10"));
        Assert.True(IpAddressValidator.IsValidIpv4("127.0.0.1"));
        Assert.False(IpAddressValidator.IsValidIpv4("999.1.1.1"));
        Assert.False(IpAddressValidator.IsValidIpv4("example.local"));
    }

    [Fact]
    public async Task Duplicate_ip_is_rejected_by_device_service()
    {
        await using var store = await TestStore.CreateAsync();
        var repository = new DeviceRepository(store.ConnectionFactory);
        var service = new DeviceService(repository);

        var first = await service.SaveAsync(CreateDevice("Switch 1", "192.0.2.10"));
        var duplicate = await service.SaveAsync(CreateDevice("Switch 2", "192.0.2.10"));

        Assert.True(first.Success);
        Assert.False(duplicate.Success);
    }

    [Fact]
    public async Task Schedule_service_accepts_switch_10_minutes_and_computer_6_hours()
    {
        await using var store = await TestStore.CreateAsync();
        var service = new SchedulePlanService(new SchedulePlanRepository(store.ConnectionFactory));

        var switchPlan = await service.SaveAsync(new SchedulePlan
        {
            Name = "Switchler 10 dakika",
            TargetType = SchedulePlanTargetType.DeviceType,
            TargetValue = DeviceType.Switch.ToStorageValue(),
            IntervalMinutes = 10,
            TimeoutMs = 1000,
            MaxParallelism = 4,
            FailureThreshold = 1,
            IsActive = true
        });
        var computerPlan = await service.SaveAsync(new SchedulePlan
        {
            Name = "Bilgisayarlar 6 saat",
            TargetType = SchedulePlanTargetType.DeviceType,
            TargetValue = DeviceType.Computer.ToStorageValue(),
            IntervalMinutes = 360,
            TimeoutMs = 1000,
            MaxParallelism = 4,
            FailureThreshold = 1,
            IsActive = true
        });

        Assert.True(switchPlan.Success);
        Assert.True(computerPlan.Success);
    }

    [Fact]
    public async Task Csv_export_sanitizes_formula_cells()
    {
        var temp = Path.Combine(Path.GetTempPath(), "nhm-csv-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(temp);
        try
        {
            var path = Path.Combine(temp, "devices.csv");
            var device = CreateDevice("=FORMULA", "192.0.2.11");
            device.Location = "+Office";
            device.Description = "@note";
            await new CsvExportService().ExportDevicesAsync(new[] { device }, path);

            var content = await File.ReadAllTextAsync(path);
            Assert.Contains("'=FORMULA", content);
            Assert.Contains("'+Office", content);
            Assert.Contains("'@note", content);
        }
        finally
        {
            Directory.Delete(temp, recursive: true);
        }
    }

    [Fact]
    public async Task Csv_import_reports_valid_invalid_and_duplicate_rows()
    {
        var temp = Path.Combine(Path.GetTempPath(), "nhm-csv-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(temp);
        try
        {
            var path = Path.Combine(temp, "import.csv");
            await File.WriteAllTextAsync(
                path,
                "Name;IpAddress;DeviceType;Location\nKamera Test;192.0.2.12;Kamera;Depo\nBozuk;999.1.1.1;Kamera;Depo\nTekrar;192.0.2.12;Kamera;Depo");

            var preview = await new CsvExportService().ReadDeviceImportPreviewAsync(path, Array.Empty<Device>());

            Assert.Equal(3, preview.TotalRows);
            Assert.Single(preview.ValidRows);
            Assert.Equal(2, preview.InvalidRowCount);
        }
        finally
        {
            Directory.Delete(temp, recursive: true);
        }
    }

    [Fact]
    public async Task Ping_cancellation_is_propagated()
    {
        var cts = new CancellationTokenSource();
        cts.Cancel();
        var ping = new PingService();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            ping.PingAsync(CreateDevice("Cancel", "192.0.2.1"), new PingOptions(5000, 1, 1), cts.Token));
    }

    [Fact]
    public void Schedule_timing_calculates_next_run()
    {
        var now = new DateTime(2026, 7, 12, 12, 0, 0);
        var plan = new SchedulePlan { IntervalMinutes = 10 };
        var timing = new ScheduleTimingService();

        Assert.True(timing.IsDue(plan, now));
        Assert.Equal(now.AddMinutes(10), timing.CalculateNextRunAfterExecution(plan, now));
    }

    [Fact]
    public async Task Overdue_plan_runs_once_and_avoids_catchup_storm()
    {
        await using var store = await TestStore.CreateAsync();
        var fakePing = new FakePingService();
        var scheduler = await CreateSchedulerAsync(store, fakePing);
        await SeedDeviceAndPlanAsync(store, "127.0.0.1", SchedulePlanTargetType.AllDevices, string.Empty, DateTime.Now.AddHours(-2));

        await scheduler.RunDuePlansOnceAsync();
        await scheduler.RunDuePlansOnceAsync();

        Assert.Equal(1, fakePing.PingCount);
        Assert.Single(await new PingLogRepository(store.ConnectionFactory).GetRecentAsync());
    }

    [Fact]
    public async Task Duplicate_schedule_targets_do_not_ping_same_device_twice()
    {
        await using var store = await TestStore.CreateAsync();
        var fakePing = new FakePingService();
        var scheduler = await CreateSchedulerAsync(store, fakePing);
        var deviceId = await AddDeviceAsync(store, CreateDevice("Switch 1", "127.0.0.1", DeviceType.Switch));
        await AddPlanAsync(store, "Plan A", SchedulePlanTargetType.Device, deviceId.ToString(), DateTime.Now.AddMinutes(-1));
        await AddPlanAsync(store, "Plan B", SchedulePlanTargetType.Device, deviceId.ToString(), DateTime.Now.AddMinutes(-1));

        await scheduler.RunDuePlansOnceAsync();

        Assert.Equal(1, fakePing.PingCount);
        Assert.Single(fakePing.PingedDeviceIds);
        var logs = await new PingLogRepository(store.ConnectionFactory).GetRecentAsync();
        Assert.Equal(2, logs.Count(log => log.TriggerType == PingTriggerType.Scheduled));
        Assert.Contains(logs, log => log.SchedulePlanName == "Plan A");
        Assert.Contains(logs, log => log.SchedulePlanName == "Plan B");
    }

    [Fact]
    public async Task Worker_restart_loads_existing_schedule_without_replay_storm()
    {
        await using var store = await TestStore.CreateAsync();
        await AddDeviceAsync(store, CreateDevice("Loopback", "127.0.0.1"));
        await AddPlanAsync(store, "Restart Plan", SchedulePlanTargetType.AllDevices, string.Empty, DateTime.Now.AddMinutes(-1));

        await using (var scheduler = await WorkerComposition.CreateSchedulerAsync(CreateWorkerOptions(store)))
        {
            await scheduler.RunDuePlansOnceAsync();
        }

        await using (var scheduler = await WorkerComposition.CreateSchedulerAsync(CreateWorkerOptions(store)))
        {
            await scheduler.RunDuePlansOnceAsync();
        }

        var logs = await new PingLogRepository(store.ConnectionFactory).GetRecentAsync();
        Assert.Single(logs);
    }

    [Fact]
    public async Task Sqlite_allows_concurrent_read_write_with_wal()
    {
        await using var store = await TestStore.CreateAsync();
        var device = CreateDevice("Concurrent", "127.0.0.1");
        device.Id = await AddDeviceAsync(store, device);
        var logs = new PingLogRepository(store.ConnectionFactory);

        var writes = Enumerable.Range(0, 20).Select(index => logs.AddAsync(CreateLog(device, PingTriggerType.Manual)));
        var reads = Enumerable.Range(0, 20).Select(_ => logs.GetRecentAsync());
        await Task.WhenAll(writes.Cast<Task>().Concat(reads.Cast<Task>()));

        Assert.True((await logs.GetRecentAsync()).Count >= 20);
    }

    [Fact]
    public async Task Legacy_localappdata_database_is_copied_not_deleted()
    {
        var root = Path.Combine(Path.GetTempPath(), "nhm-migration-" + Guid.NewGuid().ToString("N"));
        var data = Path.Combine(root, "programdata");
        var legacy = Path.Combine(root, "legacy");
        Directory.CreateDirectory(data);
        Directory.CreateDirectory(legacy);
        var legacyDb = Path.Combine(legacy, "network_health_monitor.db");
        await CreateMinimalSqliteAsync(legacyDb);

        DatabasePaths.Configure(new FixedApplicationPathProvider(data), legacy);
        await new SqliteConnectionFactory().InitializeAsync();

        Assert.True(File.Exists(Path.Combine(data, "network_health_monitor.db")));
        Assert.True(File.Exists(legacyDb));
        Assert.NotEmpty(Directory.GetFiles(Path.Combine(data, "backups"), "*.db"));
        SqliteConnection.ClearAllPools();
        Directory.Delete(root, recursive: true);
    }

    [Fact]
    public async Task Existing_programdata_database_is_not_overwritten_by_legacy_migration()
    {
        var root = Path.Combine(Path.GetTempPath(), "nhm-migration-" + Guid.NewGuid().ToString("N"));
        var data = Path.Combine(root, "programdata");
        var legacy = Path.Combine(root, "legacy");
        Directory.CreateDirectory(data);
        Directory.CreateDirectory(legacy);
        var programDataDb = Path.Combine(data, "network_health_monitor.db");
        var legacyDb = Path.Combine(legacy, "network_health_monitor.db");
        await CreateMarkerSqliteAsync(programDataDb, "ProgramDataMarker");
        await CreateMarkerSqliteAsync(legacyDb, "LegacyMarker");

        DatabasePaths.Configure(new FixedApplicationPathProvider(data), legacy);
        await new SqliteConnectionFactory().InitializeAsync();

        int markerCount;
        await using (var connection = new SqliteConnection($"Data Source={programDataDb}"))
        {
            await connection.OpenAsync();
            await using var command = connection.CreateCommand();
            command.CommandText = "SELECT COUNT(1) FROM sqlite_master WHERE type = 'table' AND name = 'ProgramDataMarker';";
            markerCount = Convert.ToInt32(await command.ExecuteScalarAsync());
        }

        Assert.Equal(1, markerCount);
        Assert.True(File.Exists(legacyDb));
        SqliteConnection.ClearAllPools();
        Directory.Delete(root, recursive: true);
    }

    [Fact]
    public async Task Manual_and_scheduled_ping_sources_are_logged()
    {
        await using var store = await TestStore.CreateAsync();
        var fakePing = new FakePingService();
        var execution = CreatePingExecution(store, fakePing);
        var device = CreateDevice("Source", "127.0.0.1");
        device.Id = await AddDeviceAsync(store, device);

        await execution.PingDevicesAsync(new[] { device }, new PingOptions(1000, 1, 1), PingTriggerType.Manual);
        await execution.PingDevicesAsync(new[] { device }, new PingOptions(1000, 1, 1), PingTriggerType.Scheduled);

        var logs = await new PingLogRepository(store.ConnectionFactory).GetRecentAsync();
        Assert.Contains(logs, log => log.TriggerType == PingTriggerType.Manual);
        Assert.Contains(logs, log => log.TriggerType == PingTriggerType.Scheduled);
    }

    private static async Task<ISchedulerService> CreateSchedulerAsync(TestStore store, FakePingService pingService)
    {
        await EnsureSettingsAsync();
        var deviceRepository = new DeviceRepository(store.ConnectionFactory);
        var deviceGroupRepository = new DeviceGroupRepository(store.ConnectionFactory);
        var scheduleRepository = new SchedulePlanRepository(store.ConnectionFactory);
        return new SchedulerService(
            deviceRepository,
            deviceGroupRepository,
            scheduleRepository,
            new PingLogRepository(store.ConnectionFactory),
            CreatePingExecution(store, pingService),
            new SchedulePlanTargetResolver(),
            new DeviceCheckPolicyService(),
            new AppSettingsService(),
            new ScheduleTimingService(),
            new SystemClock(),
            new SchedulerRuntimeOptions { PollIntervalOverride = TimeSpan.FromMilliseconds(100) });
    }

    private static IPingExecutionService CreatePingExecution(TestStore store, IPingService pingService)
    {
        return new PingExecutionService(
            new DeviceRepository(store.ConnectionFactory),
            new DeviceGroupRepository(store.ConnectionFactory),
            new PingLogRepository(store.ConnectionFactory),
            new OutageRepository(store.ConnectionFactory),
            pingService,
            new DeviceCheckPolicyService(),
            new DeviceHealthEvaluator(),
            new AppSettingsService());
    }

    private static async Task SeedDeviceAndPlanAsync(TestStore store, string ip, SchedulePlanTargetType targetType, string targetValue, DateTime nextRunAt)
    {
        await AddDeviceAsync(store, CreateDevice("Seed", ip));
        await AddPlanAsync(store, "Seed Plan", targetType, targetValue, nextRunAt);
    }

    private static async Task<int> AddDeviceAsync(TestStore store, Device device)
    {
        device.CreatedAt = DateTime.Now;
        device.UpdatedAt = DateTime.Now;
        return await new DeviceRepository(store.ConnectionFactory).AddAsync(device);
    }

    private static async Task<int> AddPlanAsync(TestStore store, string name, SchedulePlanTargetType targetType, string targetValue, DateTime nextRunAt)
    {
        return await new SchedulePlanRepository(store.ConnectionFactory).AddAsync(new SchedulePlan
        {
            Name = name,
            TargetType = targetType,
            TargetValue = targetValue,
            IntervalMinutes = 10,
            TimeoutMs = 1000,
            MaxParallelism = 1,
            FailureThreshold = 1,
            IsActive = true,
            NextRunAt = nextRunAt
        });
    }

    private static Device CreateDevice(string name, string ip, DeviceType type = DeviceType.Camera)
    {
        return new Device
        {
            Name = name,
            IpAddress = ip,
            DeviceType = type,
            IsActive = true,
            AutoCheckEnabled = true,
            LastStatus = DeviceStatus.Unknown,
            LastStableStatus = DeviceStatus.Unknown,
            CreatedAt = DateTime.Now,
            UpdatedAt = DateTime.Now
        };
    }

    private static PingLog CreateLog(Device device, PingTriggerType triggerType)
    {
        return new PingLog
        {
            DeviceId = device.Id,
            DeviceName = device.Name,
            IpAddress = device.IpAddress,
            DeviceType = device.DeviceType,
            Status = DeviceStatus.Online,
            CheckedAt = DateTime.Now,
            TriggerType = triggerType
        };
    }

    private static async Task EnsureSettingsAsync()
    {
        await new AppSettingsService().SaveAsync(AppSettings.Default);
    }

    private static async Task CreateMinimalSqliteAsync(string path)
    {
        await CreateMarkerSqliteAsync(path, "Marker");
    }

    private static async Task CreateMarkerSqliteAsync(string path, string tableName)
    {
        await using var connection = new SqliteConnection($"Data Source={path}");
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = $"CREATE TABLE {tableName} (Id INTEGER PRIMARY KEY);";
        await command.ExecuteNonQueryAsync();
    }

    private static WorkerOptions CreateWorkerOptions(TestStore store)
    {
        return new WorkerOptions
        {
            PathProvider = new FixedApplicationPathProvider(store.DataDirectory),
            LegacyDataDirectory = store.LegacyDirectory,
            PollIntervalOverride = TimeSpan.FromMilliseconds(100)
        };
    }
}
