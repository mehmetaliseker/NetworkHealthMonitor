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
            Assert.Empty(preview.ValidRows);
            Assert.Equal(1, preview.InvalidRowCount);
            Assert.Equal(2, preview.DuplicateCount);
            Assert.True(preview.HasBlockingErrors);
        }
        finally
        {
            Directory.Delete(temp, recursive: true);
        }
    }

    [Fact]
    public async Task Bulk_delete_soft_deletes_devices_and_preserves_logs()
    {
        await using var store = await TestStore.CreateAsync();
        var repository = new DeviceRepository(store.ConnectionFactory);
        var first = CreateDevice("Bulk 1", "192.0.2.60");
        var second = CreateDevice("Bulk 2", "192.0.2.61");
        first.Id = await repository.AddAsync(first);
        second.Id = await repository.AddAsync(second);
        await new PingLogRepository(store.ConnectionFactory).AddAsync(CreateLog(first, PingTriggerType.Manual));

        var affected = await repository.BulkSoftDeleteAsync(new[] { first.Id, second.Id });

        Assert.Equal(2, affected);
        Assert.Empty(await repository.GetAutoCheckCandidatesAsync());
        Assert.Equal(2, (await repository.GetAllAsync(onlyDeleted: true)).Count);
        Assert.Single(await new PingLogRepository(store.ConnectionFactory).GetRecentAsync());
    }

    [Fact]
    public async Task Bulk_delete_rolls_back_when_transaction_fails()
    {
        await using var store = await TestStore.CreateAsync();
        var repository = new DeviceRepository(store.ConnectionFactory);
        var first = CreateDevice("Rollback 1", "192.0.2.62");
        var second = CreateDevice("Rollback 2", "192.0.2.63");
        first.Id = await repository.AddAsync(first);
        second.Id = await repository.AddAsync(second);

        await using (var connection = await store.ConnectionFactory.CreateOpenConnectionAsync())
        {
            await using var command = connection.CreateCommand();
            command.CommandText = """
                CREATE TRIGGER RollbackBulkDelete
                BEFORE UPDATE OF IsDeleted ON Devices
                WHEN NEW.IpAddress = '192.0.2.63'
                BEGIN
                    SELECT RAISE(ABORT, 'forced rollback');
                END;
                """;
            await command.ExecuteNonQueryAsync();
        }

        await Assert.ThrowsAsync<SqliteException>(() => repository.BulkSoftDeleteAsync(new[] { first.Id, second.Id }));

        Assert.Equal(2, (await repository.GetAutoCheckCandidatesAsync()).Count);
        Assert.Empty(await repository.GetAllAsync(onlyDeleted: true));
    }

    [Fact]
    public async Task Group_delete_soft_deletes_only_group_devices()
    {
        await using var store = await TestStore.CreateAsync();
        var groups = new DeviceGroupRepository(store.ConnectionFactory);
        var g1 = new DeviceGroup { Name = "Group A" };
        var g2 = new DeviceGroup { Name = "Group B" };
        await groups.AddAsync(g1);
        await groups.AddAsync(g2);
        var repository = new DeviceRepository(store.ConnectionFactory);
        await repository.AddAsync(withGroup(CreateDevice("A1", "192.0.2.70"), g1));
        await repository.AddAsync(withGroup(CreateDevice("A2", "192.0.2.71"), g1));
        await repository.AddAsync(withGroup(CreateDevice("B1", "192.0.2.72"), g2));

        var affected = await repository.BulkSoftDeleteByGroupAsync(g1.Id, deleteEmptyGroup: false);

        Assert.Equal(2, affected);
        var active = await repository.GetAllAsync();
        Assert.Single(active);
        Assert.Equal("192.0.2.72", active[0].IpAddress);

        static Device withGroup(Device device, DeviceGroup group)
        {
            device.GroupId = group.Id;
            device.GroupName = group.Name;
            return device;
        }
    }

    [Fact]
    public async Task Csv_add_only_adds_new_and_keeps_existing()
    {
        await using var store = await TestStore.CreateAsync();
        var repository = new DeviceRepository(store.ConnectionFactory);
        await repository.AddAsync(CreateDevice("Existing", "192.0.2.80"));
        var path = await WriteCsvAsync("Name;IpAddress;DeviceType\nExisting Changed;192.0.2.80;Kamera\nNew;192.0.2.81;Switch");

        var service = CreateImportService(store);
        var preview = await service.ReadImportPreviewAsync(path, await repository.GetAllAsync(includeDeleted: true), new CsvImportOptions(CsvImportMode.AddOnly, CsvImportScope.AllActiveDevices, null, "", Path.GetFileName(path), "test"));
        var result = await service.ApplyImportAsync(preview, new CsvImportOptions(CsvImportMode.AddOnly, CsvImportScope.AllActiveDevices, null, "", Path.GetFileName(path), "test"));

        Assert.Equal(1, result.Added);
        Assert.Equal(1, result.Skipped);
        var devices = await repository.GetAllAsync();
        Assert.Equal(2, devices.Count);
        Assert.Contains(devices, device => device.Name == "Existing");
    }

    [Fact]
    public async Task Csv_upsert_updates_existing_and_adds_new()
    {
        await using var store = await TestStore.CreateAsync();
        var repository = new DeviceRepository(store.ConnectionFactory);
        await repository.AddAsync(CreateDevice("Existing", "192.0.2.82"));
        var path = await WriteCsvAsync("Name;IpAddress;DeviceType;Location\nUpdated;192.0.2.82;Switch;Rack\nNew;192.0.2.83;Kamera;Depo");
        var options = new CsvImportOptions(CsvImportMode.Upsert, CsvImportScope.AllActiveDevices, null, "", Path.GetFileName(path), "test");

        var service = CreateImportService(store);
        var preview = await service.ReadImportPreviewAsync(path, await repository.GetAllAsync(includeDeleted: true), options);
        var result = await service.ApplyImportAsync(preview, options);

        Assert.Equal(1, result.Added);
        Assert.Equal(1, result.Updated);
        var devices = await repository.GetAllAsync();
        Assert.Contains(devices, device => device.IpAddress == "192.0.2.82" && device.Name == "Updated" && device.DeviceType == DeviceType.Switch);
    }

    [Fact]
    public async Task Csv_sync_adds_updates_restores_and_soft_deletes_missing()
    {
        await using var store = await TestStore.CreateAsync();
        var repository = new DeviceRepository(store.ConnectionFactory);
        var keep = CreateDevice("Keep", "192.0.2.84");
        var remove = CreateDevice("Remove", "192.0.2.85");
        var restore = CreateDevice("Restore", "192.0.2.86");
        keep.Id = await repository.AddAsync(keep);
        remove.Id = await repository.AddAsync(remove);
        restore.Id = await repository.AddAsync(restore);
        await repository.SoftDeleteAsync(restore.Id, DateTime.UtcNow);
        await new PingLogRepository(store.ConnectionFactory).AddAsync(CreateLog(remove, PingTriggerType.Manual));
        var path = await WriteCsvAsync("Name;IpAddress;DeviceType\nKeep Updated;192.0.2.84;Switch\nRestore Updated;192.0.2.86;Kamera\nAdded;192.0.2.87;Server");
        var options = new CsvImportOptions(CsvImportMode.Sync, CsvImportScope.AllActiveDevices, null, "", Path.GetFileName(path), "test");
        var service = CreateImportService(store);

        var preview = await service.ReadImportPreviewAsync(path, await repository.GetAllAsync(includeDeleted: true), options);
        var result = await service.ApplyImportAsync(preview, options);

        Assert.Equal(1, result.Added);
        Assert.Equal(1, result.Updated);
        Assert.Equal(1, result.Restored);
        Assert.Equal(1, result.Deleted);
        var activeIps = (await repository.GetAllAsync()).Select(device => device.IpAddress).OrderBy(ip => ip).ToList();
        Assert.Equal(new[] { "192.0.2.84", "192.0.2.86", "192.0.2.87" }, activeIps);
        Assert.Contains(await repository.GetAllAsync(onlyDeleted: true), device => device.IpAddress == "192.0.2.85");
        Assert.Single(await new PingLogRepository(store.ConnectionFactory).GetRecentAsync());
    }

    [Fact]
    public async Task Csv_sync_empty_or_duplicate_csv_is_not_applied()
    {
        await using var store = await TestStore.CreateAsync();
        var repository = new DeviceRepository(store.ConnectionFactory);
        await repository.AddAsync(CreateDevice("Keep", "192.0.2.88"));
        var service = CreateImportService(store);
        var options = new CsvImportOptions(CsvImportMode.Sync, CsvImportScope.AllActiveDevices, null, "", "bad.csv", "test");
        var emptyPath = await WriteCsvAsync("Name;IpAddress;DeviceType\n");
        var duplicatePath = await WriteCsvAsync("Name;IpAddress;DeviceType\nA;192.0.2.88;Kamera\nB;192.0.2.88;Kamera");

        var emptyPreview = await service.ReadImportPreviewAsync(emptyPath, await repository.GetAllAsync(includeDeleted: true), options);
        var duplicatePreview = await service.ReadImportPreviewAsync(duplicatePath, await repository.GetAllAsync(includeDeleted: true), options);

        Assert.True(emptyPreview.HasBlockingErrors);
        Assert.True(duplicatePreview.HasBlockingErrors);
        await service.ApplyImportAsync(emptyPreview, options);
        await service.ApplyImportAsync(duplicatePreview, options);
        Assert.Single(await repository.GetAllAsync());
    }

    [Fact]
    public async Task Csv_group_scoped_sync_only_deletes_missing_devices_in_selected_group()
    {
        await using var store = await TestStore.CreateAsync();
        var groups = new DeviceGroupRepository(store.ConnectionFactory);
        var g1 = new DeviceGroup { Name = "Sync Group" };
        var g2 = new DeviceGroup { Name = "Other Group" };
        await groups.AddAsync(g1);
        await groups.AddAsync(g2);
        var repository = new DeviceRepository(store.ConnectionFactory);
        await repository.AddAsync(WithGroup(CreateDevice("Keep", "192.0.2.89"), g1));
        await repository.AddAsync(WithGroup(CreateDevice("Delete", "192.0.2.90"), g1));
        await repository.AddAsync(WithGroup(CreateDevice("Other", "192.0.2.91"), g2));
        var path = await WriteCsvAsync("Name;IpAddress;DeviceType\nKeep;192.0.2.89;Kamera");
        var options = new CsvImportOptions(CsvImportMode.Sync, CsvImportScope.SelectedGroup, g1.Id, g1.Name, Path.GetFileName(path), "test");
        var service = CreateImportService(store);

        var preview = await service.ReadImportPreviewAsync(path, await repository.GetAllAsync(includeDeleted: true), options);
        await service.ApplyImportAsync(preview, options);

        var activeIps = (await repository.GetAllAsync()).Select(device => device.IpAddress).OrderBy(ip => ip).ToList();
        Assert.Equal(new[] { "192.0.2.89", "192.0.2.91" }, activeIps);

        static Device WithGroup(Device device, DeviceGroup group)
        {
            device.GroupId = group.Id;
            device.GroupName = group.Name;
            return device;
        }
    }

    [Fact]
    public async Task Outbox_retry_failed_resets_to_pending_and_sent_is_not_retried()
    {
        await using var store = await TestStore.CreateAsync();
        var repository = new NotificationOutboxRepository(store.ConnectionFactory);
        var failedId = await repository.AddPendingAsync("Test", null, null, "{}", "failed-" + Guid.NewGuid(), DateTime.UtcNow);
        var sentId = await repository.AddPendingAsync("Test", null, null, "{}", "sent-" + Guid.NewGuid(), DateTime.UtcNow);
        await repository.MarkFailedAsync(failedId, 3, "401 Unauthorized");
        await repository.MarkSentAsync(sentId, DateTime.UtcNow);

        var affected = await repository.RetryFailedAsync(new[] { failedId, sentId }, DateTime.UtcNow);

        Assert.Equal(1, affected);
        var items = await repository.GetFilteredAsync(null, null, null, null, null, 10);
        Assert.Contains(items, item => item.Id == failedId && item.Status == "Pending" && item.AttemptCount == 0 && item.LockedAtUtc is null && item.LockedBy == "");
        Assert.Contains(items, item => item.Id == sentId && item.Status == "Sent");
    }

    [Fact]
    public async Task Ui_autostart_creates_single_shortcut_and_removes_it()
    {
        var root = Path.Combine(Path.GetTempPath(), "nhm-autostart-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var exe = Path.Combine(root, "NetworkHealthMonitor.exe");
            await File.WriteAllTextAsync(exe, "fake");
            var service = new WindowsStartupShortcutService(root);

            await service.SetEnabledAsync(true, exe);
            await service.SetEnabledAsync(true, exe);

            Assert.True(File.Exists(service.ShortcutPath));
            Assert.Single(Directory.GetFiles(root, "NetworkHealthMonitor*.lnk"));
            Assert.True(service.IsEnabled(exe));

            await service.SetEnabledAsync(false, exe);

            Assert.False(File.Exists(service.ShortcutPath));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
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

        Assert.True(File.Exists(Path.Combine(data, "data", "network_health_monitor.db")));
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
        var programDataDb = Path.Combine(data, "data", "network_health_monitor.db");
        var legacyDb = Path.Combine(legacy, "network_health_monitor.db");
        Directory.CreateDirectory(Path.GetDirectoryName(programDataDb)!);
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
    public async Task Clean_database_schema_contract_and_startup_queries_succeed()
    {
        await using var store = await TestStore.CreateAsync();
        var now = DateTime.UtcNow;

        await store.ConnectionFactory.VerifySchemaAsync();
        await AssertColumnExistsAsync(store, "Devices", "IsDeleted");
        await AssertColumnExistsAsync(store, "Devices", "DeletedAtUtc");
        await AssertColumnExistsAsync(store, "Devices", "IsEnabled");
        await AssertColumnExistsAsync(store, "PingLogs", "IsReachable");
        await AssertColumnExistsAsync(store, "PingLogs", "ErrorCode");
        await AssertColumnExistsAsync(store, "PingLogs", "Source");
        await AssertColumnExistsAsync(store, "PingLogs", "PlanId");
        await AssertColumnExistsAsync(store, "PingLogs", "WorkerInstanceId");
        await AssertColumnExistsAsync(store, "DeviceIncidents", "EndedAtUtc");
        await AssertMigrationRecordedAsync(store, DatabaseMigrationRunner.DeviceIncidentsEndedAtUtcMigrationId);

        var availability = new AvailabilityRepository(store.ConnectionFactory);
        Assert.Empty(await new DeviceRepository(store.ConnectionFactory).GetAllAsync(includeDeleted: true));
        Assert.Empty(await new SchedulePlanRepository(store.ConnectionFactory).GetAllAsync());
        Assert.Empty(await new PingLogRepository(store.ConnectionFactory).GetRecentAsync());
        Assert.Empty(await availability.GetSummaryAsync(now.AddDays(-1), now, TimeZoneInfo.Local.Id, includeDeleted: true));
        _ = await availability.GetDashboardSummaryAsync(now);
        Assert.Empty(await availability.GetIncidentRankingAsync(now.AddDays(-30), now, 10));
        _ = await new NotificationOutboxRepository(store.ConnectionFactory).GetCountsAsync();
        Assert.Null(await new WorkerHeartbeatRepository(store.ConnectionFactory).GetLatestAsync());
    }

    [Fact]
    public async Task Partial_core_server_migration_adds_ended_at_utc_and_preserves_incident_data()
    {
        var root = Path.Combine(Path.GetTempPath(), "nhm-tests-" + Guid.NewGuid().ToString("N"));
        var programData = Path.Combine(root, "programdata");
        var legacy = Path.Combine(root, "legacy-localappdata");
        try
        {
            DatabasePaths.Configure(new FixedApplicationPathProvider(programData), legacy);
            await CreatePartialCoreServerDatabaseAsync(DatabasePaths.DatabaseFilePath);

            var factory = new SqliteConnectionFactory();
            await factory.InitializeAsync();
            await factory.InitializeAsync();

            await using var connection = await factory.CreateOpenConnectionAsync();
            await using var command = connection.CreateCommand();
            command.CommandText = "SELECT EndedAtUtc FROM DeviceIncidents WHERE Id = 1;";
            var endedAt = Convert.ToString(await command.ExecuteScalarAsync());

            Assert.Equal("2026-07-15T13:00:00.0000000Z", endedAt);
            await AssertColumnExistsAsync(factory, "DeviceIncidents", "EndedAtUtc");
            await AssertMigrationRecordedAsync(factory, DatabaseMigrationRunner.DeviceIncidentsEndedAtUtcMigrationId);
            Assert.Single(await new AvailabilityRepository(factory).GetIncidentRankingAsync(DateTime.UtcNow.AddDays(-30), DateTime.UtcNow, 10));
        }
        finally
        {
            TryDeleteDirectory(root);
        }
    }

    [Fact]
    public async Task Very_old_localappdata_database_is_backed_up_migrated_and_queryable()
    {
        var root = Path.Combine(Path.GetTempPath(), "nhm-tests-" + Guid.NewGuid().ToString("N"));
        var programData = Path.Combine(root, "programdata");
        var legacy = Path.Combine(root, "legacy-localappdata");
        try
        {
            DatabasePaths.Configure(new FixedApplicationPathProvider(programData), legacy);
            await CreateVeryOldDatabaseAsync(DatabasePaths.LegacyDatabaseFilePath, "Legacy Device", "192.0.2.210");

            var factory = new SqliteConnectionFactory();
            await factory.InitializeAsync();

            Assert.True(File.Exists(DatabasePaths.DatabaseFilePath));
            Assert.True(Directory.EnumerateFiles(DatabasePaths.BackupDirectory, "legacy-localappdata-*.db").Any());
            Assert.Single(await new DeviceRepository(factory).GetAllAsync(includeDeleted: true));
            Assert.Single(await new PingLogRepository(factory).GetRecentAsync());
            await factory.VerifySchemaAsync();
        }
        finally
        {
            TryDeleteDirectory(root);
        }
    }

    [Fact]
    public async Task Programdata_root_database_is_copied_to_data_directory_before_migrations()
    {
        var root = Path.Combine(Path.GetTempPath(), "nhm-tests-" + Guid.NewGuid().ToString("N"));
        var programData = Path.Combine(root, "programdata");
        var legacy = Path.Combine(root, "legacy-localappdata");
        try
        {
            DatabasePaths.Configure(new FixedApplicationPathProvider(programData), legacy);
            await CreateVeryOldDatabaseAsync(DatabasePaths.LegacyProgramDataDatabaseFilePath, "Root Device", "192.0.2.211");

            var factory = new SqliteConnectionFactory();
            await factory.InitializeAsync();

            Assert.True(File.Exists(DatabasePaths.DatabaseFilePath));
            Assert.True(File.Exists(DatabasePaths.LegacyProgramDataDatabaseFilePath));
            Assert.True(Directory.EnumerateFiles(DatabasePaths.BackupDirectory, "programdata-root-*.db").Any());
            Assert.Contains(await new DeviceRepository(factory).GetAllAsync(includeDeleted: true), device => device.IpAddress == "192.0.2.211");
            await factory.VerifySchemaAsync();
        }
        finally
        {
            TryDeleteDirectory(root);
        }
    }

    [Fact]
    public async Task Concurrent_initialization_applies_migration_once_and_keeps_sqlite_integrity()
    {
        var root = Path.Combine(Path.GetTempPath(), "nhm-tests-" + Guid.NewGuid().ToString("N"));
        var programData = Path.Combine(root, "programdata");
        var legacy = Path.Combine(root, "legacy-localappdata");
        try
        {
            DatabasePaths.Configure(new FixedApplicationPathProvider(programData), legacy);
            var first = new SqliteConnectionFactory();
            var second = new SqliteConnectionFactory();

            await Task.WhenAll(first.InitializeAsync(), second.InitializeAsync());

            await first.VerifySchemaAsync();
            Assert.Equal(1, await CountMigrationAsync(first, DatabaseMigrationRunner.DeviceIncidentsEndedAtUtcMigrationId));
            await AssertIntegrityOkAsync(first);
        }
        finally
        {
            TryDeleteDirectory(root);
        }
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

    [Fact]
    public async Task Soft_deleted_device_is_excluded_and_can_be_restored()
    {
        await using var store = await TestStore.CreateAsync();
        var repository = new DeviceRepository(store.ConnectionFactory);
        var device = CreateDevice("Delete Me", "192.0.2.50");
        var id = await repository.AddAsync(device);

        await repository.SoftDeleteAsync(id, DateTime.UtcNow);

        Assert.Empty(await repository.GetAutoCheckCandidatesAsync());
        Assert.Single(await repository.GetAllAsync(onlyDeleted: true));

        await repository.RestoreAsync(id);

        Assert.Single(await repository.GetAutoCheckCandidatesAsync());
        Assert.Empty(await repository.GetAllAsync(onlyDeleted: true));
    }

    [Fact]
    public async Task Scheduler_does_not_ping_soft_deleted_device()
    {
        await using var store = await TestStore.CreateAsync();
        var fakePing = new FakePingService();
        var scheduler = await CreateSchedulerAsync(store, fakePing);
        var id = await AddDeviceAsync(store, CreateDevice("Deleted", "127.0.0.2"));
        await AddPlanAsync(store, "Deleted Plan", SchedulePlanTargetType.AllDevices, string.Empty, DateTime.Now.AddMinutes(-1));
        await new DeviceRepository(store.ConnectionFactory).SoftDeleteAsync(id, DateTime.UtcNow);

        await scheduler.RunDuePlansOnceAsync();

        Assert.Equal(0, fakePing.PingCount);
        Assert.Empty(await new PingLogRepository(store.ConnectionFactory).GetRecentAsync());
    }

    [Fact]
    public async Task Three_failures_create_one_down_incident_and_one_outbox_item()
    {
        await using var store = await TestStore.CreateAsync();
        var fakePing = new FakePingService(false, false, false, false);
        var execution = CreatePingExecution(store, fakePing);
        var device = CreateDevice("Down", "127.0.0.3");
        device.Id = await AddDeviceAsync(store, device);

        for (var index = 0; index < 4; index++)
        {
            await execution.PingDevicesAsync(new[] { device }, new PingOptions(1000, 1, 3), PingTriggerType.Scheduled);
        }

        Assert.Equal(1, await CountOpenIncidentsAsync(store));
        Assert.Equal(1, await CountOutboxAsync(store, "DeviceDown"));
    }

    [Fact]
    public async Task Two_failures_do_not_create_down_incident()
    {
        await using var store = await TestStore.CreateAsync();
        var fakePing = new FakePingService(false, false);
        var execution = CreatePingExecution(store, fakePing);
        var device = CreateDevice("Warn", "127.0.0.4");
        device.Id = await AddDeviceAsync(store, device);

        await execution.PingDevicesAsync(new[] { device }, new PingOptions(1000, 1, 3), PingTriggerType.Scheduled);
        await execution.PingDevicesAsync(new[] { device }, new PingOptions(1000, 1, 3), PingTriggerType.Scheduled);

        Assert.Equal(0, await CountOpenIncidentsAsync(store));
        Assert.Equal(0, await CountOutboxAsync(store, "DeviceDown"));
    }

    [Fact]
    public async Task Recovery_after_two_successes_closes_incident_and_creates_one_recovery_notification()
    {
        await using var store = await TestStore.CreateAsync();
        var fakePing = new FakePingService(false, false, false, true, true, true);
        var execution = CreatePingExecution(store, fakePing);
        var device = CreateDevice("Recover", "127.0.0.5");
        device.Id = await AddDeviceAsync(store, device);

        for (var index = 0; index < 5; index++)
        {
            await execution.PingDevicesAsync(new[] { device }, new PingOptions(1000, 1, 3), PingTriggerType.Scheduled);
        }

        Assert.Equal(0, await CountOpenIncidentsAsync(store));
        Assert.Equal(1, await CountOutboxAsync(store, "DeviceDown"));
        Assert.Equal(1, await CountOutboxAsync(store, "DeviceRecovered"));
        Assert.Equal(1, await CountClosedIncidentsWithEndedAtAsync(store));
    }

    [Fact]
    public void Retry_backoff_honors_retry_after()
    {
        var service = new AlertPolicyService();
        var now = new DateTime(2026, 7, 15, 10, 0, 0, DateTimeKind.Utc);

        var next = service.CalculateNextRetryUtc(3, 30, TimeSpan.FromSeconds(90), now);

        Assert.Equal(now.AddSeconds(90), next);
    }

    [Fact]
    public async Task Ntfy_client_classifies_unauthorized_as_permanent_failure()
    {
        var client = new NtfyNotificationClient(
            new TestHttpClientFactory(_ => new HttpResponseMessage(System.Net.HttpStatusCode.Unauthorized)),
            new DpapiSecretProtector());

        var result = await client.PublishAsync(new NotificationSettings
        {
            Enabled = true,
            BaseUrl = "https://ntfy.example",
            Topic = "topic"
        }, new NtfyNotificationPayload { Title = "t", Message = "m" });

        Assert.False(result.Success);
        Assert.False(result.IsTransient);
        Assert.Equal(401, result.StatusCode);
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

    private static async Task<int> CountOpenIncidentsAsync(TestStore store)
    {
        await using var connection = await store.ConnectionFactory.CreateOpenConnectionAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT COUNT(1) FROM DeviceIncidents WHERE Status = 'Open';";
        return Convert.ToInt32(await command.ExecuteScalarAsync());
    }

    private static async Task<int> CountClosedIncidentsWithEndedAtAsync(TestStore store)
    {
        await using var connection = await store.ConnectionFactory.CreateOpenConnectionAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT COUNT(1) FROM DeviceIncidents WHERE Status = 'Closed' AND EndedAtUtc IS NOT NULL;";
        return Convert.ToInt32(await command.ExecuteScalarAsync());
    }

    private static async Task<int> CountOutboxAsync(TestStore store, string eventType)
    {
        await using var connection = await store.ConnectionFactory.CreateOpenConnectionAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT COUNT(1) FROM NotificationOutbox WHERE EventType = @EventType;";
        command.Parameters.AddWithValue("@EventType", eventType);
        return Convert.ToInt32(await command.ExecuteScalarAsync());
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
            new AppSettingsService(),
            new IncidentService(store.ConnectionFactory, new AppSettingsService(), new AlertPolicyService()));
    }

    private static DeviceImportExportService CreateImportService(TestStore store)
    {
        return new DeviceImportExportService(
            new CsvExportService(),
            new DeviceRepository(store.ConnectionFactory),
            new DataMaintenanceService(store.ConnectionFactory));
    }

    private static async Task<string> WriteCsvAsync(string content)
    {
        var directory = Path.Combine(Path.GetTempPath(), "nhm-csv-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, "devices.csv");
        await File.WriteAllTextAsync(path, content);
        return path;
    }

    [Fact]
    public async Task Availability_periods_track_first_failure_confirmation_and_recovery()
    {
        await using var store = await TestStore.CreateAsync();
        var repository = new DeviceRepository(store.ConnectionFactory);
        var device = CreateDevice("Availability", "192.0.2.130");
        device.Id = await repository.AddAsync(device);
        var availability = new AvailabilityRepository(store.ConnectionFactory);
        var t0 = new DateTime(2026, 7, 15, 10, 0, 0, DateTimeKind.Utc);
        var t1 = t0.AddMinutes(1);
        var t2 = t0.AddMinutes(2);
        var t3 = t0.AddMinutes(3);
        var t4 = t0.AddMinutes(4);

        await ApplyAvailabilityPingAsync(availability, device, true, DeviceStatus.Online, t0);
        await ApplyAvailabilityPingAsync(availability, device, false, DeviceStatus.Warning, t1);
        await ApplyAvailabilityPingAsync(availability, device, false, DeviceStatus.UnderWatch, t2);
        await ApplyAvailabilityPingAsync(availability, device, false, DeviceStatus.Offline, t3);
        await ApplyAvailabilityPingAsync(availability, device, true, DeviceStatus.Online, t4);

        var periods = await availability.GetPeriodsAsync(device.Id, t0.AddMinutes(-1), t4.AddMinutes(1));
        var down = Assert.Single(periods, period => period.Status == AvailabilityStatus.Down);
        var open = Assert.Single(periods, period => period.EndedAtUtc is null);

        Assert.Equal(t1, down.StartedAtUtc);
        Assert.Equal(t3, down.ConfirmedAtUtc);
        Assert.Equal(t1, down.FirstFailureAtUtc);
        Assert.Equal(120, (long)(down.ConfirmedAtUtc!.Value - down.FirstFailureAtUtc!.Value).TotalSeconds);
        Assert.Equal(t4, down.EndedAtUtc);
        Assert.Equal(AvailabilityStatus.Up, open.Status);
    }

    [Fact]
    public async Task Confirmed_down_policy_can_start_downtime_at_confirmation_time()
    {
        await using var store = await TestStore.CreateAsync();
        var repository = new DeviceRepository(store.ConnectionFactory);
        var device = CreateDevice("Confirmed Policy", "192.0.2.131");
        device.Id = await repository.AddAsync(device);
        var availability = new AvailabilityRepository(store.ConnectionFactory);
        var t0 = new DateTime(2026, 7, 15, 10, 0, 0, DateTimeKind.Utc);
        var t1 = t0.AddMinutes(1);
        var t3 = t0.AddMinutes(3);

        await ApplyAvailabilityPingAsync(availability, device, true, DeviceStatus.Online, t0);
        await ApplyAvailabilityPingAsync(availability, device, false, DeviceStatus.Warning, t1);
        await ApplyAvailabilityPingAsync(availability, device, false, DeviceStatus.Offline, t3, DowntimeStartPolicy.ConfirmedDownTime);

        var down = Assert.Single(await availability.GetPeriodsAsync(device.Id, t0, t3.AddMinutes(1)), period => period.Status == AvailabilityStatus.Down);

        Assert.Equal(t3, down.StartedAtUtc);
        Assert.Equal(t1, down.FirstFailureAtUtc);
        Assert.Equal(t3, down.ConfirmedAtUtc);
    }

    [Fact]
    public async Task Worker_heartbeat_gap_creates_unknown_period_without_backfilling_up()
    {
        await using var store = await TestStore.CreateAsync();
        var repository = new DeviceRepository(store.ConnectionFactory);
        var device = CreateDevice("Heartbeat Gap", "192.0.2.132");
        device.Id = await repository.AddAsync(device);
        var availability = new AvailabilityRepository(store.ConnectionFactory);
        var t0 = new DateTime(2026, 7, 15, 10, 0, 0, DateTimeKind.Utc);

        await ApplyAvailabilityPingAsync(availability, device, true, DeviceStatus.Online, t0);
        await availability.ReconcileWorkerHeartbeatGapAsync(t0.AddSeconds(15), t0.AddMinutes(10), 120);

        var periods = await availability.GetPeriodsAsync(device.Id, t0, t0.AddMinutes(11));
        var unknown = Assert.Single(periods, period => period.Status == AvailabilityStatus.Unknown);

        Assert.Equal(t0.AddSeconds(135), unknown.StartedAtUtc);
        Assert.Null(unknown.EndedAtUtc);
    }

    [Fact]
    public async Task Maintenance_window_is_excluded_from_down_and_recorded_as_maintenance()
    {
        await using var store = await TestStore.CreateAsync();
        var repository = new DeviceRepository(store.ConnectionFactory);
        var device = CreateDevice("Maintenance", "192.0.2.133");
        device.Id = await repository.AddAsync(device);
        var availability = new AvailabilityRepository(store.ConnectionFactory);
        var maintenanceRepository = new MaintenanceWindowRepository(store.ConnectionFactory);
        var t0 = new DateTime(2026, 7, 15, 10, 0, 0, DateTimeKind.Utc);
        var maintenanceStart = t0.AddMinutes(10);
        var maintenanceEnd = t0.AddMinutes(20);

        await ApplyAvailabilityPingAsync(availability, device, true, DeviceStatus.Online, t0);
        await maintenanceRepository.AddAsync(
            new MaintenanceWindow
            {
                Name = "Planned",
                StartedAtUtc = maintenanceStart,
                EndedAtUtc = maintenanceEnd,
                Status = MaintenanceWindowStatus.Active,
                CreatedBy = "test"
            },
            new[] { new MaintenanceWindowTarget { TargetType = MonitoringTargetType.AllDevices } });
        await availability.ReconcileMaintenanceWindowsAsync(maintenanceStart.AddSeconds(1));
        await availability.ReconcileMaintenanceWindowsAsync(maintenanceEnd.AddSeconds(1));
        await availability.RecalculateDailyAsync(DateOnly.FromDateTime(t0), DateOnly.FromDateTime(t0), TimeZoneInfo.Utc.Id, device.Id);

        var daily = await ReadDailyAsync(store, device.Id, DateOnly.FromDateTime(t0), TimeZoneInfo.Utc.Id);

        Assert.True(daily.MaintenanceSeconds >= 600);
        Assert.Equal(0, daily.DownSeconds);
    }

    [Fact]
    public async Task Daily_aggregate_is_idempotent_and_calculates_availability_coverage_and_delay()
    {
        await using var store = await TestStore.CreateAsync();
        var repository = new DeviceRepository(store.ConnectionFactory);
        var device = CreateDevice("Daily", "192.0.2.134");
        device.Id = await repository.AddAsync(device);
        var availability = new AvailabilityRepository(store.ConnectionFactory);
        var day = new DateTime(2026, 7, 15, 0, 0, 0, DateTimeKind.Utc);

        await InsertAvailabilityPeriodAsync(store, device.Id, AvailabilityStatus.Up, day, day.AddHours(20));
        await InsertAvailabilityPeriodAsync(store, device.Id, AvailabilityStatus.Down, day.AddHours(20), day.AddHours(22), firstFailureAtUtc: day.AddHours(20), confirmedAtUtc: day.AddHours(20).AddMinutes(2));
        await availability.RecalculateDailyAsync(DateOnly.FromDateTime(day), DateOnly.FromDateTime(day), TimeZoneInfo.Utc.Id, device.Id);
        await availability.RecalculateDailyAsync(DateOnly.FromDateTime(day), DateOnly.FromDateTime(day), TimeZoneInfo.Utc.Id, device.Id);

        var count = await CountDailyAsync(store, device.Id, DateOnly.FromDateTime(day), TimeZoneInfo.Utc.Id);
        var daily = await ReadDailyAsync(store, device.Id, DateOnly.FromDateTime(day), TimeZoneInfo.Utc.Id);

        Assert.Equal(1, count);
        Assert.Equal(72000, daily.UpSeconds);
        Assert.Equal(7200, daily.DownSeconds);
        Assert.Equal(7200, daily.UnknownSeconds);
        Assert.Equal(120, daily.TotalDetectionDelaySeconds);
        Assert.Equal(90.909, Math.Round(daily.AvailabilityPercent!.Value, 3));
    }

    [Fact]
    public async Task Dashboard_uses_time_weighted_availability_not_simple_device_average()
    {
        await using var store = await TestStore.CreateAsync();
        var repository = new DeviceRepository(store.ConnectionFactory);
        var first = CreateDevice("Weighted 1", "192.0.2.135");
        var second = CreateDevice("Weighted 2", "192.0.2.136");
        first.Id = await repository.AddAsync(first);
        second.Id = await repository.AddAsync(second);
        var start = DateTime.UtcNow.AddHours(-2);
        var end = DateTime.UtcNow;
        await InsertAvailabilityPeriodAsync(store, first.Id, AvailabilityStatus.Up, start, start.AddMinutes(90));
        await InsertAvailabilityPeriodAsync(store, first.Id, AvailabilityStatus.Down, start.AddMinutes(90), start.AddMinutes(100));
        await InsertAvailabilityPeriodAsync(store, second.Id, AvailabilityStatus.Up, start, start.AddMinutes(1));
        await InsertAvailabilityPeriodAsync(store, second.Id, AvailabilityStatus.Down, start.AddMinutes(1), start.AddMinutes(10));

        var service = new AvailabilityService(new AvailabilityRepository(store.ConnectionFactory));
        var summary = await service.GetAvailabilitySummaryAsync(start, end, TimeZoneInfo.Utc.Id);
        var weighted = summary.Sum(item => item.UpSeconds) * 100d / summary.Sum(item => item.UpSeconds + item.DownSeconds);
        var simpleAverage = summary.Average(item => item.AvailabilityPercent!.Value);

        Assert.NotEqual(Math.Round(simpleAverage, 3), Math.Round(weighted, 3));
        Assert.Equal(82.727, Math.Round(weighted, 3));
    }

    [Fact]
    public async Task Availability_summary_csv_contains_professional_columns_and_utf8_bom()
    {
        var temp = Path.Combine(Path.GetTempPath(), "nhm-availability-csv-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(temp);
        try
        {
            var path = Path.Combine(temp, "availability.csv");
            await new CsvExportService().ExportAvailabilitySummaryAsync(
                new[]
                {
                    new AvailabilitySummaryReportItem
                    {
                        DeviceId = 1,
                        DeviceName = "Kamera Çatı",
                        IpAddress = "192.0.2.140",
                        DeviceType = DeviceType.Camera,
                        GroupName = "Çatı",
                        ReportStartUtc = DateTime.UtcNow.AddDays(-1),
                        ReportEndUtc = DateTime.UtcNow,
                        ExpectedMonitoringSeconds = 86400,
                        UpSeconds = 86000,
                        DownSeconds = 400,
                        AvailabilityPercent = 99.537,
                        StrictAvailabilityPercent = 99.537,
                        CoveragePercent = 100,
                        CurrentStatus = AvailabilityStatus.Up,
                        CurrentStatusSinceUtc = DateTime.UtcNow.AddHours(-1),
                        SlaTargetPercent = 99.9,
                        SlaStatus = "Ihlal"
                    }
                },
                path);

            var bytes = await File.ReadAllBytesAsync(path);
            var content = await File.ReadAllTextAsync(path, System.Text.Encoding.UTF8);

            Assert.Equal(new byte[] { 0xEF, 0xBB, 0xBF }, bytes.Take(3).ToArray());
            Assert.Contains("AvailabilityPercent", content);
            Assert.Contains("StrictAvailabilityPercent", content);
            Assert.Contains("CoveragePercent", content);
            Assert.Contains("MTTRSeconds", content);
            Assert.Contains("Kamera Çatı", content);
        }
        finally
        {
            Directory.Delete(temp, recursive: true);
        }
    }

    [Fact]
    public async Task Timeline_periods_are_returned_in_start_order()
    {
        await using var store = await TestStore.CreateAsync();
        var repository = new DeviceRepository(store.ConnectionFactory);
        var device = CreateDevice("Timeline", "192.0.2.170");
        device.Id = await repository.AddAsync(device);
        var now = DateTime.UtcNow;
        await InsertAvailabilityPeriodAsync(store, device.Id, AvailabilityStatus.Up, now.AddHours(-3), now.AddHours(-2));
        await InsertAvailabilityPeriodAsync(store, device.Id, AvailabilityStatus.Down, now.AddHours(-2), now.AddHours(-1));

        var timeline = await new AvailabilityService(new AvailabilityRepository(store.ConnectionFactory)).GetTimelineAsync(device.Id, now.AddHours(-4), now);

        Assert.Collection(
            timeline,
            first => Assert.Equal(AvailabilityStatus.Up, first.Status),
            second => Assert.Equal(AvailabilityStatus.Down, second.Status));
    }

    [Fact]
    public async Task Maintenance_crud_persists_targets_and_status_changes()
    {
        await using var store = await TestStore.CreateAsync();
        var groups = new DeviceGroupRepository(store.ConnectionFactory);
        var group = new DeviceGroup { Name = "Bakim Grubu" };
        await groups.AddAsync(group);
        var repository = new MaintenanceWindowRepository(store.ConnectionFactory);
        var id = await repository.AddAsync(
            new MaintenanceWindow
            {
                Name = "Planli Bakim",
                StartedAtUtc = DateTime.UtcNow.AddMinutes(-5),
                EndedAtUtc = DateTime.UtcNow.AddHours(1),
                Reason = "Test",
                SuppressNotifications = true,
                ContinuePings = false,
                Status = MaintenanceWindowStatus.Active
            },
            new[] { new MaintenanceWindowTarget { TargetType = MonitoringTargetType.Group, TargetId = group.Id } });

        var item = Assert.Single(await repository.GetAllAsync());
        Assert.Equal(id, item.Window.Id);
        Assert.False(item.Window.ContinuePings);
        Assert.Equal(MonitoringTargetType.Group, Assert.Single(item.Targets).TargetType);

        await repository.CompleteAsync(id);
        item = Assert.Single(await repository.GetAllAsync());
        Assert.Equal(MaintenanceWindowStatus.Completed, item.Window.Status);
    }

    [Fact]
    public async Task Monitoring_calendar_crud_persists_rules_assignments_and_default()
    {
        await using var store = await TestStore.CreateAsync();
        var repository = new MonitoringCalendarRepository(store.ConnectionFactory);
        var id = await repository.AddAsync(
            new MonitoringCalendar { Name = "Hafta Ici", TimezoneId = TimeZoneInfo.Local.Id, IsDefault = true },
            new[]
            {
                new MonitoringCalendarRule { DayOfWeek = DayOfWeek.Monday, StartTime = TimeSpan.FromHours(8), EndTime = TimeSpan.FromHours(18), IsEnabled = true }
            },
            new[] { new DeviceMonitoringCalendarAssignment { TargetType = MonitoringTargetType.AllDevices } });

        var item = (await repository.GetAllAsync()).First(calendar => calendar.Calendar.Id == id);
        Assert.True(item.Calendar.IsDefault);
        Assert.Single(item.Rules);
        Assert.Single(item.Assignments);

        await repository.UpdateAsync(
            new MonitoringCalendar
            {
                Id = item.Calendar.Id,
                Name = item.Calendar.Name,
                TimezoneId = item.Calendar.TimezoneId,
                IsDefault = item.Calendar.IsDefault,
                CreatedAtUtc = item.Calendar.CreatedAtUtc,
                UpdatedAtUtc = DateTime.UtcNow
            },
            new[]
            {
                new MonitoringCalendarRule { DayOfWeek = DayOfWeek.Tuesday, StartTime = TimeSpan.FromHours(9), EndTime = TimeSpan.FromHours(17), IsEnabled = true }
            },
            Array.Empty<DeviceMonitoringCalendarAssignment>());

        item = (await repository.GetAllAsync()).First(calendar => calendar.Calendar.Id == id);
        Assert.Equal(DayOfWeek.Tuesday, Assert.Single(item.Rules).DayOfWeek);
    }

    [Fact]
    public async Task Group_sla_is_inherited_when_device_has_no_override()
    {
        await using var store = await TestStore.CreateAsync();
        var groups = new DeviceGroupRepository(store.ConnectionFactory);
        var group = new DeviceGroup { Name = "SLA Grup", TargetAvailabilityPercent = 99.9 };
        await groups.AddAsync(group);
        var devices = new DeviceRepository(store.ConnectionFactory);
        var device = CreateDevice("Inherited SLA", "192.0.2.171");
        device.GroupId = group.Id;
        device.GroupName = group.Name;
        device.Id = await devices.AddAsync(device);
        var now = DateTime.UtcNow;
        await InsertAvailabilityPeriodAsync(store, device.Id, AvailabilityStatus.Up, now.AddHours(-1), now);

        var summary = Assert.Single(await new AvailabilityRepository(store.ConnectionFactory).GetSummaryAsync(now.AddHours(-1), now, TimeZoneInfo.Local.Id));

        Assert.Equal(99.9, summary.SlaTargetPercent);
    }

    [Fact]
    public async Task Device_sla_override_wins_over_group_sla()
    {
        await using var store = await TestStore.CreateAsync();
        var groups = new DeviceGroupRepository(store.ConnectionFactory);
        var group = new DeviceGroup { Name = "SLA Grup", TargetAvailabilityPercent = 99 };
        await groups.AddAsync(group);
        var devices = new DeviceRepository(store.ConnectionFactory);
        var device = CreateDevice("Override SLA", "192.0.2.172");
        device.GroupId = group.Id;
        device.GroupName = group.Name;
        device.SlaTargetAvailabilityPercent = 99.99;
        device.Id = await devices.AddAsync(device);
        var now = DateTime.UtcNow;
        await InsertAvailabilityPeriodAsync(store, device.Id, AvailabilityStatus.Up, now.AddHours(-1), now);

        var summary = Assert.Single(await new AvailabilityRepository(store.ConnectionFactory).GetSummaryAsync(now.AddHours(-1), now, TimeZoneInfo.Local.Id));

        Assert.Equal(99.99, summary.SlaTargetPercent);
    }

    [Fact]
    public void Service_readiness_maps_stale_heartbeat_to_unhealthy()
    {
        var check = SystemReadinessService.MapHeartbeatFreshness(DateTime.UtcNow.AddMinutes(-10), DateTime.UtcNow, 120, serviceRunning: true);

        Assert.Equal(ReadinessLevel.Fail, check.Level);
        Assert.Contains("heartbeat eski", check.Detail);
    }

    [Fact]
    public void Service_status_mapping_detects_automatic_running_and_recovery()
    {
        var status = WindowsServiceStatusService.ParseStatus(
            "STATE              : 4  RUNNING",
            "START_TYPE         : 2   AUTO_START",
            "FAILURE_ACTIONS    : RESTART -- Delay = 60000 milliseconds.");

        Assert.True(status.IsRunning);
        Assert.True(status.IsAutomaticStartup);
        Assert.True(status.RecoveryActionsConfigured);
    }

    [Fact]
    public void Production_readiness_script_parses()
    {
        var script = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "scripts", "production-readiness-test.ps1"));
        var process = System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
        {
            FileName = "powershell",
            Arguments = $"-NoProfile -ExecutionPolicy Bypass -Command \"[scriptblock]::Create((Get-Content -LiteralPath '{script}' -Raw)) | Out-Null\"",
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true
        });
        Assert.NotNull(process);
        process!.WaitForExit(10000);
        Assert.Equal(0, process.ExitCode);
    }

    [Fact]
    public void Version_mismatch_is_detected()
    {
        Assert.False(SystemReadinessService.IsVersionMatch("1.2.3", "1.2.4"));
        Assert.True(SystemReadinessService.IsVersionMatch("1.2.3.0", "1.2.3"));
    }

    private static async Task ApplyAvailabilityPingAsync(
        AvailabilityRepository availability,
        Device device,
        bool isSuccess,
        DeviceStatus status,
        DateTime checkedAtUtc,
        DowntimeStartPolicy policy = DowntimeStartPolicy.FirstFailedCheck)
    {
        var log = new PingLog
        {
            DeviceId = device.Id,
            DeviceName = device.Name,
            IpAddress = device.IpAddress,
            DeviceType = device.DeviceType,
            GroupName = device.GroupName,
            Status = status,
            IsReachable = isSuccess,
            CheckedAt = checkedAtUtc,
            TriggerType = PingTriggerType.Scheduled
        };
        var result = new PingDeviceResult(device, isSuccess, isSuccess ? 1 : null, checkedAtUtc, string.Empty, isSuccess ? string.Empty : "failed", status);
        await availability.ApplyPingResultsAsync(new[] { result }, new Dictionary<int, PingLog> { [device.Id] = log }, policy);
    }

    private static async Task InsertAvailabilityPeriodAsync(
        TestStore store,
        int deviceId,
        AvailabilityStatus status,
        DateTime startedAtUtc,
        DateTime endedAtUtc,
        DateTime? firstFailureAtUtc = null,
        DateTime? confirmedAtUtc = null)
    {
        await using var connection = await store.ConnectionFactory.CreateOpenConnectionAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO DeviceAvailabilityPeriods
                (DeviceId, Status, StartedAtUtc, EndedAtUtc, DurationSeconds, IncidentId,
                 ReasonCode, ReasonText, DetectionSource, FirstFailureAtUtc, ConfirmedAtUtc,
                 CreatedAtUtc, UpdatedAtUtc)
            VALUES
                (@DeviceId, @Status, @StartedAtUtc, @EndedAtUtc, @DurationSeconds, NULL,
                 'Test', '', 'Test', @FirstFailureAtUtc, @ConfirmedAtUtc,
                 @CreatedAtUtc, @UpdatedAtUtc);
            """;
        command.Parameters.AddWithValue("@DeviceId", deviceId);
        command.Parameters.AddWithValue("@Status", status.ToStorageValue());
        command.Parameters.AddWithValue("@StartedAtUtc", startedAtUtc.ToUniversalTime().ToString("O"));
        command.Parameters.AddWithValue("@EndedAtUtc", endedAtUtc.ToUniversalTime().ToString("O"));
        command.Parameters.AddWithValue("@DurationSeconds", Math.Max(0, (long)(endedAtUtc - startedAtUtc).TotalSeconds));
        command.Parameters.AddWithValue("@FirstFailureAtUtc", firstFailureAtUtc.HasValue ? firstFailureAtUtc.Value.ToUniversalTime().ToString("O") : DBNull.Value);
        command.Parameters.AddWithValue("@ConfirmedAtUtc", confirmedAtUtc.HasValue ? confirmedAtUtc.Value.ToUniversalTime().ToString("O") : DBNull.Value);
        command.Parameters.AddWithValue("@CreatedAtUtc", DateTime.UtcNow.ToString("O"));
        command.Parameters.AddWithValue("@UpdatedAtUtc", DateTime.UtcNow.ToString("O"));
        await command.ExecuteNonQueryAsync();
    }

    private static async Task<int> CountDailyAsync(TestStore store, int deviceId, DateOnly date, string timezoneId)
    {
        await using var connection = await store.ConnectionFactory.CreateOpenConnectionAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT COUNT(1)
            FROM DeviceAvailabilityDaily
            WHERE DeviceId = @DeviceId
              AND Date = @Date
              AND TimezoneId = @TimezoneId;
            """;
        command.Parameters.AddWithValue("@DeviceId", deviceId);
        command.Parameters.AddWithValue("@Date", date.ToString("yyyy-MM-dd"));
        command.Parameters.AddWithValue("@TimezoneId", timezoneId);
        return Convert.ToInt32(await command.ExecuteScalarAsync());
    }

    private static async Task<DeviceAvailabilityDaily> ReadDailyAsync(TestStore store, int deviceId, DateOnly date, string timezoneId)
    {
        await using var connection = await store.ConnectionFactory.CreateOpenConnectionAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT Id, DeviceId, Date, TimezoneId, ExpectedMonitoringSeconds, UpSeconds, DownSeconds,
                   UnknownSeconds, MaintenanceSeconds, PausedSeconds, IncidentCount, RecoveredIncidentCount,
                   LongestOutageSeconds, TotalDetectionDelaySeconds, AvailabilityPercent,
                   StrictAvailabilityPercent, CoveragePercent, CalculatedAtUtc, CalculationVersion
            FROM DeviceAvailabilityDaily
            WHERE DeviceId = @DeviceId
              AND Date = @Date
              AND TimezoneId = @TimezoneId
            LIMIT 1;
            """;
        command.Parameters.AddWithValue("@DeviceId", deviceId);
        command.Parameters.AddWithValue("@Date", date.ToString("yyyy-MM-dd"));
        command.Parameters.AddWithValue("@TimezoneId", timezoneId);
        await using var reader = await command.ExecuteReaderAsync();
        Assert.True(await reader.ReadAsync());
        return new DeviceAvailabilityDaily
        {
            Id = reader.GetInt64(0),
            DeviceId = reader.GetInt32(1),
            Date = DateOnly.Parse(reader.GetString(2)),
            TimezoneId = reader.GetString(3),
            ExpectedMonitoringSeconds = reader.GetInt64(4),
            UpSeconds = reader.GetInt64(5),
            DownSeconds = reader.GetInt64(6),
            UnknownSeconds = reader.GetInt64(7),
            MaintenanceSeconds = reader.GetInt64(8),
            PausedSeconds = reader.GetInt64(9),
            IncidentCount = reader.GetInt32(10),
            RecoveredIncidentCount = reader.GetInt32(11),
            LongestOutageSeconds = reader.GetInt64(12),
            TotalDetectionDelaySeconds = reader.GetInt64(13),
            AvailabilityPercent = reader.IsDBNull(14) ? null : reader.GetDouble(14),
            StrictAvailabilityPercent = reader.IsDBNull(15) ? null : reader.GetDouble(15),
            CoveragePercent = reader.IsDBNull(16) ? null : reader.GetDouble(16),
            CalculatedAtUtc = DateTime.Parse(reader.GetString(17)),
            CalculationVersion = reader.GetInt32(18)
        };
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

    private static async Task CreatePartialCoreServerDatabaseAsync(string path)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        await using var connection = new SqliteConnection($"Data Source={path}");
        await connection.OpenAsync();
        await ExecuteNonQueryAsync(connection, """
            CREATE TABLE SchemaMigrations (
                Version TEXT PRIMARY KEY,
                AppliedAtUtc TEXT NOT NULL
            );
            """);
        await ExecuteNonQueryAsync(connection, """
            INSERT INTO SchemaMigrations (Version, AppliedAtUtc)
            VALUES ('2026071501-core-server-schema', '2026-07-15T00:00:00.0000000Z');
            """);
        await ExecuteNonQueryAsync(connection, """
            CREATE TABLE Devices (
                Id INTEGER PRIMARY KEY AUTOINCREMENT,
                Name TEXT NOT NULL,
                IpAddress TEXT NOT NULL UNIQUE,
                DeviceType TEXT NOT NULL,
                GroupName TEXT NOT NULL DEFAULT '',
                CreatedAt TEXT NOT NULL,
                UpdatedAt TEXT NOT NULL
            );
            """);
        await ExecuteNonQueryAsync(connection, """
            INSERT INTO Devices (Id, Name, IpAddress, DeviceType, GroupName, CreatedAt, UpdatedAt)
            VALUES (1, 'Partial Incident Device', '192.0.2.220', 'Camera', 'Partial', '2026-07-15T10:00:00.0000000Z', '2026-07-15T10:00:00.0000000Z');
            """);
        await ExecuteNonQueryAsync(connection, """
            CREATE TABLE DeviceIncidents (
                Id INTEGER PRIMARY KEY AUTOINCREMENT,
                DeviceId INTEGER NOT NULL,
                StartedAtUtc TEXT NOT NULL,
                RecoveredAtUtc TEXT NULL,
                Status TEXT NOT NULL,
                InitialFailureCount INTEGER NOT NULL DEFAULT 0,
                CurrentFailureCount INTEGER NOT NULL DEFAULT 0,
                RecoverySuccessCount INTEGER NOT NULL DEFAULT 0,
                FirstFailureAtUtc TEXT NULL,
                ConfirmedDownAtUtc TEXT NULL,
                DetectionDelaySeconds INTEGER NOT NULL DEFAULT 0,
                LastFailureAtUtc TEXT NULL,
                LastSuccessAtUtc TEXT NULL,
                DownNotificationCreatedAtUtc TEXT NULL,
                RecoveryNotificationCreatedAtUtc TEXT NULL,
                FlapCount INTEGER NOT NULL DEFAULT 0,
                CreatedAtUtc TEXT NOT NULL,
                UpdatedAtUtc TEXT NOT NULL,
                FOREIGN KEY (DeviceId) REFERENCES Devices(Id) ON DELETE CASCADE
            );
            """);
        await ExecuteNonQueryAsync(connection, """
            INSERT INTO DeviceIncidents
                (Id, DeviceId, StartedAtUtc, RecoveredAtUtc, Status, InitialFailureCount,
                 CurrentFailureCount, RecoverySuccessCount, FirstFailureAtUtc, ConfirmedDownAtUtc,
                 DetectionDelaySeconds, LastFailureAtUtc, LastSuccessAtUtc,
                 DownNotificationCreatedAtUtc, RecoveryNotificationCreatedAtUtc, FlapCount,
                 CreatedAtUtc, UpdatedAtUtc)
            VALUES
                (1, 1, '2026-07-15T12:00:00.0000000Z', '2026-07-15T13:00:00.0000000Z', 'Closed', 3,
                 3, 2, '2026-07-15T11:58:00.0000000Z', '2026-07-15T12:00:00.0000000Z',
                 120, '2026-07-15T12:00:00.0000000Z', '2026-07-15T13:00:00.0000000Z',
                 NULL, NULL, 0, '2026-07-15T12:00:00.0000000Z', '2026-07-15T13:00:00.0000000Z');
            """);
    }

    private static async Task CreateVeryOldDatabaseAsync(string path, string deviceName, string ipAddress)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        await using var connection = new SqliteConnection($"Data Source={path}");
        await connection.OpenAsync();
        await ExecuteNonQueryAsync(connection, """
            CREATE TABLE Devices (
                Id INTEGER PRIMARY KEY AUTOINCREMENT,
                Name TEXT NOT NULL,
                IpAddress TEXT NOT NULL UNIQUE,
                DeviceType TEXT NOT NULL,
                GroupName TEXT NOT NULL DEFAULT '',
                CreatedAt TEXT NOT NULL,
                UpdatedAt TEXT NOT NULL
            );
            """);
        await using (var insertDevice = connection.CreateCommand())
        {
            insertDevice.CommandText = """
                INSERT INTO Devices (Id, Name, IpAddress, DeviceType, GroupName, CreatedAt, UpdatedAt)
                VALUES (1, @Name, @IpAddress, 'Camera', 'Legacy', '2026-07-15T10:00:00.0000000Z', '2026-07-15T10:00:00.0000000Z');
                """;
            insertDevice.Parameters.AddWithValue("@Name", deviceName);
            insertDevice.Parameters.AddWithValue("@IpAddress", ipAddress);
            await insertDevice.ExecuteNonQueryAsync();
        }

        await ExecuteNonQueryAsync(connection, """
            CREATE TABLE PingLogs (
                Id INTEGER PRIMARY KEY AUTOINCREMENT,
                DeviceId INTEGER NULL,
                DeviceName TEXT NOT NULL,
                IpAddress TEXT NOT NULL,
                DeviceType TEXT NOT NULL,
                Status TEXT NOT NULL,
                LatencyMs INTEGER NULL,
                CheckedAt TEXT NOT NULL
            );
            """);
        await using var insertLog = connection.CreateCommand();
        insertLog.CommandText = """
            INSERT INTO PingLogs (DeviceId, DeviceName, IpAddress, DeviceType, Status, LatencyMs, CheckedAt)
            VALUES (1, @Name, @IpAddress, 'Camera', 'Online', 1, '2026-07-15T10:01:00.0000000Z');
            """;
        insertLog.Parameters.AddWithValue("@Name", deviceName);
        insertLog.Parameters.AddWithValue("@IpAddress", ipAddress);
        await insertLog.ExecuteNonQueryAsync();
    }

    private static async Task ExecuteNonQueryAsync(SqliteConnection connection, string commandText)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = commandText;
        await command.ExecuteNonQueryAsync();
    }

    private static Task AssertColumnExistsAsync(TestStore store, string tableName, string columnName)
    {
        return AssertColumnExistsAsync(store.ConnectionFactory, tableName, columnName);
    }

    private static async Task AssertColumnExistsAsync(SqliteConnectionFactory factory, string tableName, string columnName)
    {
        await using var connection = await factory.CreateOpenConnectionAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = $"PRAGMA table_info({tableName});";
        await using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            if (string.Equals(reader.GetString(1), columnName, StringComparison.OrdinalIgnoreCase))
            {
                return;
            }
        }

        Assert.Fail($"Expected column {tableName}.{columnName} was not found.");
    }

    private static Task AssertMigrationRecordedAsync(TestStore store, string migrationId)
    {
        return AssertMigrationRecordedAsync(store.ConnectionFactory, migrationId);
    }

    private static async Task AssertMigrationRecordedAsync(SqliteConnectionFactory factory, string migrationId)
    {
        Assert.Equal(1, await CountMigrationAsync(factory, migrationId));
    }

    private static async Task<int> CountMigrationAsync(SqliteConnectionFactory factory, string migrationId)
    {
        await using var connection = await factory.CreateOpenConnectionAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT COUNT(1) FROM SchemaMigrations WHERE Version = @Version;";
        command.Parameters.AddWithValue("@Version", migrationId);
        return Convert.ToInt32(await command.ExecuteScalarAsync());
    }

    private static async Task AssertIntegrityOkAsync(SqliteConnectionFactory factory)
    {
        await using var connection = await factory.CreateOpenConnectionAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = "PRAGMA integrity_check;";
        Assert.Equal("ok", Convert.ToString(await command.ExecuteScalarAsync()));
    }

    private static void TryDeleteDirectory(string path)
    {
        try
        {
            SqliteConnection.ClearAllPools();
            if (Directory.Exists(path))
            {
                Directory.Delete(path, recursive: true);
            }
        }
        catch
        {
            // Best effort cleanup for temp test data.
        }
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

    private sealed class TestHttpClientFactory : IHttpClientFactory
    {
        private readonly Func<HttpRequestMessage, HttpResponseMessage> _handler;

        public TestHttpClientFactory(Func<HttpRequestMessage, HttpResponseMessage> handler)
        {
            _handler = handler;
        }

        public HttpClient CreateClient(string name)
        {
            return new HttpClient(new DelegateHandler(_handler));
        }
    }

    private sealed class DelegateHandler : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, HttpResponseMessage> _handler;

        public DelegateHandler(Func<HttpRequestMessage, HttpResponseMessage> handler)
        {
            _handler = handler;
        }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            return Task.FromResult(_handler(request));
        }
    }
}
