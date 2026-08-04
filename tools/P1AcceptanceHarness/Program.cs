using System.Diagnostics;
using System.Globalization;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using Microsoft.Data.Sqlite;
using NetworkHealthMonitor.Data;
using NetworkHealthMonitor.Models;
using NetworkHealthMonitor.Services;

const string Prefix = "NHR-ACCEPTANCE-";
const string GroupName = "NHR-ACCEPTANCE-P1";
const string ServiceName = WorkerServiceConstants.ServiceName;

var artifactsRoot = args.FirstOrDefault(arg => arg.StartsWith("--artifacts=", StringComparison.OrdinalIgnoreCase))?.Split('=', 2)[1]
    ?? Path.Combine("artifacts", "p1-runtime");
var dataRoot = args.FirstOrDefault(arg => arg.StartsWith("--data-dir=", StringComparison.OrdinalIgnoreCase))?.Split('=', 2)[1];
var consoleWorker = args.Any(arg => string.Equals(arg, "--console-worker", StringComparison.OrdinalIgnoreCase));
var workerExeOverride = args.FirstOrDefault(arg => arg.StartsWith("--worker-exe=", StringComparison.OrdinalIgnoreCase))?.Split('=', 2)[1];
Directory.CreateDirectory(artifactsRoot);
if (!string.IsNullOrWhiteSpace(dataRoot))
{
    dataRoot = Path.GetFullPath(dataRoot);
}

DatabasePaths.Configure(
    string.IsNullOrWhiteSpace(dataRoot)
        ? new ProgramDataApplicationPathProvider()
        : new FixedApplicationPathProvider(dataRoot),
    null);
var report = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase)
{
    ["startedAtUtc"] = DateTime.UtcNow.ToString("O", CultureInfo.InvariantCulture),
    ["databasePath"] = DatabasePaths.DatabaseFilePath,
    ["settingsPath"] = DatabasePaths.SettingsFilePath,
    ["dataRoot"] = DatabasePaths.RootDirectory
};
ServiceState? initialService = null;
Process? consoleWorkerProcess = null;

var queryOnly = args.Any(arg => string.Equals(arg, "--query-only", StringComparison.OrdinalIgnoreCase));
var cleanupOnly = args.Any(arg => string.Equals(arg, "--cleanup-only", StringComparison.OrdinalIgnoreCase));
var prepareBulkUi = args.Any(arg => string.Equals(arg, "--prepare-bulk-ui", StringComparison.OrdinalIgnoreCase));
var p2Ops = args.Any(arg => string.Equals(arg, "--p2-ops", StringComparison.OrdinalIgnoreCase));
if (p2Ops)
{
    var connectionFactory = new SqliteConnectionFactory();
    await connectionFactory.InitializeAsync();
    var p2Report = await RunP2OperationalChecksAsync(connectionFactory, artifactsRoot);
    Console.WriteLine(JsonSerializer.Serialize(p2Report, new JsonSerializerOptions { WriteIndented = true }));
    return 0;
}

if (queryOnly || cleanupOnly || prepareBulkUi)
{
    var connectionFactory = new SqliteConnectionFactory();
    await connectionFactory.InitializeAsync();
    if (cleanupOnly || prepareBulkUi)
    {
        await CleanupAcceptanceDataAsync(connectionFactory);
    }

    if (prepareBulkUi)
    {
        var bulkSettingsBackupPath = Path.Combine(artifactsRoot, "settings-before-bulk-ui.json");
        if (File.Exists(DatabasePaths.SettingsFilePath))
        {
            File.Copy(DatabasePaths.SettingsFilePath, bulkSettingsBackupPath, overwrite: true);
        }

        await ConfigureBulkUiAcceptanceSettingsAsync();
        var deviceRepository = new DeviceRepository(connectionFactory);
        var deviceService = new DeviceService(deviceRepository);
        await SaveBulkUiDeviceAsync(deviceService, $"{Prefix}BULK-{AcceptanceTestSettings.OnlineToken}", "127.0.0.1");
        await SaveBulkUiDeviceAsync(deviceService, $"{Prefix}BULK-{AcceptanceTestSettings.OfflineToken}", "nhr-acceptance-offline.invalid");
        await SaveBulkUiDeviceAsync(deviceService, $"{Prefix}BULK-{AcceptanceTestSettings.TimeoutToken}", "nhr-acceptance-timeout.invalid");
    }

    var summary = await BuildAcceptanceSummaryAsync(connectionFactory);
    summary["mode"] = prepareBulkUi ? "prepare-bulk-ui" : cleanupOnly ? "cleanup-only" : "query-only";
    summary["databasePath"] = DatabasePaths.DatabaseFilePath;
    Console.WriteLine(JsonSerializer.Serialize(summary, new JsonSerializerOptions { WriteIndented = true }));
    return 0;
}

var originalSettingsText = File.Exists(DatabasePaths.SettingsFilePath)
    ? await File.ReadAllTextAsync(DatabasePaths.SettingsFilePath)
    : string.Empty;
var settingsBackupPath = Path.Combine(artifactsRoot, "settings-before-p1-runtime.json");
await File.WriteAllTextAsync(settingsBackupPath, originalSettingsText);
report["settingsBackupPath"] = settingsBackupPath;

await using var httpServer = await FakeHttpServer.StartAsync();
await using var smtpServer = await FakeSmtpServer.StartAsync();
report["fakeNtfyUrl"] = httpServer.Url;
report["fakeSmtpPort"] = smtpServer.Port;

try
{
    var connectionFactory = new SqliteConnectionFactory();
    await connectionFactory.InitializeAsync();
    await CleanupAcceptanceDataAsync(connectionFactory);
    await ConfigureAcceptanceSettingsAsync(httpServer.Url, smtpServer.Port);

    if (consoleWorker)
    {
        var workerExecutablePath = ResolveWorkerExecutablePath(workerExeOverride);
        report["workerExecutablePath"] = workerExecutablePath;
        consoleWorkerProcess = StartConsoleWorker(workerExecutablePath, DatabasePaths.RootDirectory);
        report["consoleWorkerPid"] = consoleWorkerProcess.Id;
        await WaitUntilAsync(
            async () => !consoleWorkerProcess.HasExited && await ReadHeartbeatAgeSecondsAsync(connectionFactory) <= 5,
            TimeSpan.FromSeconds(30),
            "Console Worker did not write a heartbeat before duplicate process verification.");
        report["heartbeatAgeBeforeDuplicateSeconds"] = await ReadHeartbeatAgeSecondsAsync(connectionFactory);
        var duplicateExitCode = await RunDuplicateConsoleWorkerAsync(workerExecutablePath, DatabasePaths.RootDirectory);
        report["duplicateWorkerExitCode"] = duplicateExitCode;
        if (duplicateExitCode != 2)
        {
            throw new InvalidOperationException($"Duplicate Worker process returned {duplicateExitCode}; expected single-instance exit code 2.");
        }
    }
    else
    {
        initialService = await ReadServiceStateAsync();
        report["initialService"] = initialService;
        if (!string.Equals(initialService.Status, "Running", StringComparison.OrdinalIgnoreCase))
        {
            await RunProcessAsync("sc.exe", $"start \"{ServiceName}\"");
            await WaitForServiceStatusAsync("RUNNING", TimeSpan.FromSeconds(30));
        }
    }

    var deviceRepository = new DeviceRepository(connectionFactory);
    var deviceService = new DeviceService(deviceRepository);
    var planRepository = new SchedulePlanRepository(connectionFactory);
    var planService = new SchedulePlanService(planRepository);
    var outboxRepository = new NotificationOutboxRepository(connectionFactory);

    var onlineDevice = await SaveDeviceAsync(deviceService, $"{Prefix}ONLINE", "127.0.0.1");
    var offlineDevice = await SaveDeviceAsync(deviceService, $"{Prefix}OFFLINE", "nhr-acceptance-offline.invalid");
    var plan = new SchedulePlan
    {
        Name = $"{Prefix}P1-WORKER-GROUP",
        TargetType = SchedulePlanTargetType.DeviceGroup,
        TargetValue = GroupName,
        ScheduleMode = ScheduleMode.FixedInterval,
        IntervalValue = 1,
        IntervalUnit = ScheduleIntervalUnit.Minutes,
        IntervalMinutes = 1,
        TimeoutMs = 500,
        MaxParallelism = 2,
        FailureThreshold = 1,
        IsActive = true,
        NextRunAt = DateTime.UtcNow.AddSeconds(-1),
        FailureRetryEnabled = true,
        ConfirmationRetryCount = 1,
        ConfirmationRetryIntervalSeconds = 10,
        OfflineRecheckIntervalSeconds = 60
    };
    var planResult = await planService.SaveAsync(plan);
    if (!planResult.Success)
    {
        throw new InvalidOperationException(planResult.Message);
    }

    report["createdOnlineDeviceId"] = onlineDevice.Id;
    report["createdOfflineDeviceId"] = offlineDevice.Id;
    report["createdPlanId"] = plan.Id;
    report["workerCandidatesAfterCreate"] = await CountAsync(connectionFactory, """
        SELECT COUNT(1)
        FROM Devices
        WHERE Name LIKE 'NHR-ACCEPTANCE-%'
          AND IsDeleted = 0
          AND IsEnabled = 1
          AND IsActive = 1
          AND AutoCheckEnabled = 1;
        """);

    await WaitUntilAsync(
        async () => await CountLogsForAcceptanceDevicesAsync(connectionFactory) >= 2,
        TimeSpan.FromSeconds(50),
        "Worker did not write initial scheduled ping logs.");
    report["logsAfterInitialWorkerRun"] = await CountLogsForAcceptanceDevicesAsync(connectionFactory);
    report["onlineStatusAfterInitialRun"] = await ReadDeviceStatusAsync(connectionFactory, onlineDevice.Id);
    report["offlineStatusAfterInitialRun"] = await ReadDeviceStatusAsync(connectionFactory, offlineDevice.Id);

    var incidentOpened = await WaitUntilOrFalseAsync(
        async () => await CountOpenIncidentsAsync(connectionFactory, offlineDevice.Id) == 1,
        TimeSpan.FromSeconds(150));
    if (!incidentOpened)
    {
        report["logsWhenIncidentOpenTimedOut"] = await CountLogsForAcceptanceDevicesAsync(connectionFactory);
        report["offlineStatusWhenIncidentOpenTimedOut"] = await ReadDeviceStatusAsync(connectionFactory, offlineDevice.Id);
        report["planStatusWhenIncidentOpenTimedOut"] = await ScalarStringAsync(connectionFactory, "SELECT LastStatus FROM SchedulePlans WHERE Id = @PlanId;", ("@PlanId", plan.Id));
        report["planLastRunAtWhenIncidentOpenTimedOut"] = await ScalarStringAsync(connectionFactory, "SELECT COALESCE(LastRunAt, '') FROM SchedulePlans WHERE Id = @PlanId;", ("@PlanId", plan.Id));
        report["planNextRunAtWhenIncidentOpenTimedOut"] = await ScalarStringAsync(connectionFactory, "SELECT COALESCE(NextRunAt, '') FROM SchedulePlans WHERE Id = @PlanId;", ("@PlanId", plan.Id));
        throw new TimeoutException("Worker retry did not open an incident for the acceptance offline device.");
    }
    report["openIncidentCount"] = await CountOpenIncidentsAsync(connectionFactory, offlineDevice.Id);
    report["outboxAfterIncidentOpen"] = await CountOutboxForAcceptanceAsync(connectionFactory);

    await WaitUntilAsync(
        async () => await CountSentOutboxForAcceptanceAsync(connectionFactory) >= 2,
        TimeSpan.FromSeconds(60),
        "Worker dispatcher did not send local ntfy and SMTP notifications.");
    report["sentOutboxAfterIncidentOpen"] = await CountSentOutboxForAcceptanceAsync(connectionFactory);
    report["fakeNtfyReceivedAfterIncidentOpen"] = httpServer.RequestCount;
    report["fakeSmtpReceivedAfterIncidentOpen"] = smtpServer.MessageCount;

    var updatedOffline = await deviceRepository.GetByIdAsync(offlineDevice.Id);
    if (updatedOffline is null)
    {
        throw new InvalidOperationException("Acceptance offline device disappeared before recovery test.");
    }

    var deleteOnlineResult = await deviceService.DeleteAsync(onlineDevice);
    if (!deleteOnlineResult.Success)
    {
        throw new InvalidOperationException(deleteOnlineResult.Message);
    }

    report["onlineDeviceDeletedBeforeRecovery"] = true;
    await UpdateDeviceAddressDirectAsync(connectionFactory, onlineDevice.Id, "nhr-acceptance-deleted.local");
    await UpdateDeviceForRecoveryDirectAsync(
        connectionFactory,
        updatedOffline.Id,
        "NHR-ACCEPTANCE-ONLINE-RECOVERED",
        "127.0.0.1",
        "P1 runtime acceptance recovery data");
    updatedOffline.IpAddress = "127.0.0.1";

    var incidentClosed = await WaitUntilOrFalseAsync(
        async () => await CountOpenIncidentsAsync(connectionFactory, offlineDevice.Id) == 0
            && await CountClosedIncidentsAsync(connectionFactory, offlineDevice.Id) >= 1,
        TimeSpan.FromSeconds(150));
    if (!incidentClosed)
    {
        report["logsWhenRecoveryTimedOut"] = await CountLogsForAcceptanceDevicesAsync(connectionFactory);
        report["offlineStatusWhenRecoveryTimedOut"] = await ReadDeviceStatusAsync(connectionFactory, offlineDevice.Id);
        report["offlineAddressWhenRecoveryTimedOut"] = await ScalarStringAsync(connectionFactory, "SELECT IpAddress FROM Devices WHERE Id = @DeviceId;", ("@DeviceId", offlineDevice.Id));
        report["openIncidentCountWhenRecoveryTimedOut"] = await CountOpenIncidentsAsync(connectionFactory, offlineDevice.Id);
        report["closedIncidentCountWhenRecoveryTimedOut"] = await CountClosedIncidentsAsync(connectionFactory, offlineDevice.Id);
        report["incidentSnapshotWhenRecoveryTimedOut"] = await BuildIncidentSnapshotAsync(connectionFactory, offlineDevice.Id);
        report["recentLogsWhenRecoveryTimedOut"] = await BuildRecentPingLogSnapshotAsync(connectionFactory, offlineDevice.Id);
        report["planStatusWhenRecoveryTimedOut"] = await ScalarStringAsync(connectionFactory, "SELECT LastStatus FROM SchedulePlans WHERE Id = @PlanId;", ("@PlanId", plan.Id));
        report["planLastRunAtWhenRecoveryTimedOut"] = await ScalarStringAsync(connectionFactory, "SELECT COALESCE(LastRunAt, '') FROM SchedulePlans WHERE Id = @PlanId;", ("@PlanId", plan.Id));
        report["planNextRunAtWhenRecoveryTimedOut"] = await ScalarStringAsync(connectionFactory, "SELECT COALESCE(NextRunAt, '') FROM SchedulePlans WHERE Id = @PlanId;", ("@PlanId", plan.Id));
        throw new TimeoutException("Worker did not close the acceptance incident after the device recovered.");
    }
    report["closedIncidentCount"] = await CountClosedIncidentsAsync(connectionFactory, offlineDevice.Id);

    await WaitUntilAsync(
        async () => await CountSentOutboxForAcceptanceAsync(connectionFactory) >= 4,
        TimeSpan.FromSeconds(60),
        "Worker dispatcher did not send local recovery notifications.");
    report["sentOutboxAfterRecovery"] = await CountSentOutboxForAcceptanceAsync(connectionFactory);
    report["fakeNtfyReceivedAfterRecovery"] = httpServer.RequestCount;
    report["fakeSmtpReceivedAfterRecovery"] = smtpServer.MessageCount;

    var restartBlocked = false;
    try
    {
        if (consoleWorker)
        {
            await StopConsoleWorkerAsync(consoleWorkerProcess);
            consoleWorkerProcess = null;
            report["consoleWorkerAfterStop"] = "Stopped";
        }
        else
        {
            await StopWorkerAsync();
            report["serviceAfterStop"] = await ReadServiceStateAsync();
        }

        report["heartbeatAgeAfterStopSeconds"] = await ReadHeartbeatAgeSecondsAsync(connectionFactory);

        await outboxRepository.AddPendingAsync(
            new NotificationOutboxCreateRequest
            {
                EventType = NotificationEventTypes.Test,
                Channel = NotificationChannels.Ntfy,
                Recipient = "nhm-p1-local",
                Subject = "NetworkHealthMonitor kabul testi",
                Body = "Bu bildirim gercek bir cihaz kesintisi degildir.",
                PayloadJson = JsonSerializer.Serialize(new NtfyNotificationPayload
                {
                    EventType = NotificationEventTypes.Test,
                    Title = "NetworkHealthMonitor kabul testi",
                    Message = "Bu bildirim gercek bir cihaz kesintisi degildir.",
                    Priority = "default",
                    Tags = "test"
                }),
                IdempotencyKey = $"{Prefix.ToLowerInvariant()}restart-outbox-ntfy"
            },
            DateTime.UtcNow);
        report["pendingOutboxInsertedWhileStopped"] = await CountPendingOutboxForAcceptanceAsync(connectionFactory);

        if (consoleWorker)
        {
            var workerExecutablePath = ResolveWorkerExecutablePath(workerExeOverride);
            consoleWorkerProcess = StartConsoleWorker(workerExecutablePath, DatabasePaths.RootDirectory);
            report["consoleWorkerPidAfterRestart"] = consoleWorkerProcess.Id;
        }
        else
        {
            await StartWorkerAsync();
        }

        await WaitUntilAsync(
            async () => await CountPendingOutboxForAcceptanceAsync(connectionFactory) == 0,
            TimeSpan.FromSeconds(45),
            "Pending outbox was not processed after Worker restart.");
        report["pendingOutboxAfterRestart"] = await CountPendingOutboxForAcceptanceAsync(connectionFactory);
        if (consoleWorker)
        {
            report["consoleWorkerAfterRestartRunning"] = consoleWorkerProcess is not null && !consoleWorkerProcess.HasExited;
        }
        else
        {
            report["serviceAfterRestart"] = await ReadServiceStateAsync();
        }

        report["heartbeatAgeAfterRestartSeconds"] = await ReadHeartbeatAgeSecondsAsync(connectionFactory);
    }
    catch (Exception ex) when (!consoleWorker && IsAccessDenied(ex.Message))
    {
        restartBlocked = true;
        report["workerRestartStatus"] = "BLOCKED";
        report["workerRestartBlocker"] = ex.Message;
        report["serviceAfterRestartBlock"] = await ReadServiceStateAsync();
        report["heartbeatAgeAfterRestartBlockSeconds"] = await ReadHeartbeatAgeSecondsAsync(connectionFactory);
    }

    report["workerProcessCount"] = await CountWorkerProcessesAsync();
    report["consoleWorkerProcessCount"] = await CountConsoleWorkerProcessesAsync();
    report["startupTypeAfterRestart"] = consoleWorker
        ? "NOT_APPLICABLE_CONSOLE_WORKER"
        : (await ReadServiceStateAsync()).StartMode;
    report["duplicateOpenIncidentCount"] = await CountDuplicateOpenIncidentsAsync(connectionFactory);
    report["duplicateOutboxIdempotencyCount"] = await CountDuplicateOutboxIdempotencyAsync(connectionFactory);
    report["status"] = restartBlocked ? "PARTIAL" : "PASS";
}
catch (Exception ex)
{
    report["status"] = "FAIL";
    report["error"] = ex.ToString();
}
finally
{
    try
    {
        await StopConsoleWorkerAsync(consoleWorkerProcess);
        consoleWorkerProcess = null;
    }
    catch (Exception ex)
    {
        report["consoleWorkerStopError"] = ex.Message;
    }

    try
    {
        var connectionFactory = new SqliteConnectionFactory();
        await CleanupAcceptanceDataAsync(connectionFactory);
    }
    catch (Exception ex)
    {
        report["cleanupError"] = ex.Message;
    }

    try
    {
        if (string.IsNullOrWhiteSpace(originalSettingsText))
        {
            if (File.Exists(DatabasePaths.SettingsFilePath))
            {
                File.Delete(DatabasePaths.SettingsFilePath);
            }
        }
        else
        {
            await File.WriteAllTextAsync(DatabasePaths.SettingsFilePath, originalSettingsText);
        }
    }
    catch (Exception ex)
    {
        report["settingsRestoreError"] = ex.Message;
    }

    try
    {
        if (!consoleWorker && initialService is not null)
        {
            var currentService = await ReadServiceStateAsync();
            if (string.Equals(initialService.Status, "Running", StringComparison.OrdinalIgnoreCase)
                && !string.Equals(currentService.Status, "Running", StringComparison.OrdinalIgnoreCase))
            {
                await StartWorkerAsync();
            }
            else if (!string.Equals(initialService.Status, "Running", StringComparison.OrdinalIgnoreCase)
                && string.Equals(currentService.Status, "Running", StringComparison.OrdinalIgnoreCase))
            {
                await StopWorkerAsync();
            }

            report["serviceStateAfterRestore"] = await ReadServiceStateAsync();
        }
    }
    catch (Exception ex)
    {
        report["workerStateRestoreError"] = ex.Message;
    }

    report["endedAtUtc"] = DateTime.UtcNow.ToString("O", CultureInfo.InvariantCulture);
    var reportPath = Path.Combine(artifactsRoot, "p1-runtime-report.json");
    await File.WriteAllTextAsync(reportPath, JsonSerializer.Serialize(report, new JsonSerializerOptions { WriteIndented = true }));
    Console.WriteLine(File.ReadAllText(reportPath));
}

return string.Equals(Convert.ToString(report["status"], CultureInfo.InvariantCulture), "PASS", StringComparison.OrdinalIgnoreCase)
    || string.Equals(Convert.ToString(report["status"], CultureInfo.InvariantCulture), "PARTIAL", StringComparison.OrdinalIgnoreCase)
    ? 0
    : 1;

static async Task ConfigureAcceptanceSettingsAsync(string ntfyUrl, int smtpPort)
{
    var settingsService = new AppSettingsService();
    var settings = await settingsService.LoadAsync();
    settings.AcceptanceTest.Enabled = true;
    settings.AcceptanceTest.DeviceNamePrefix = Prefix;
    settings.AutoCheckEnabled = true;
    settings.SchedulerPollIntervalSeconds = 5;
    settings.PingTimeoutMs = 500;
    settings.MaxParallelPings = 2;
    settings.DefaultFailureThreshold = 1;
    settings.DefaultFailureRetryLimit = 1;
    settings.DefaultFailureRetryIntervalSeconds = 10;
    settings.Notifications.Enabled = true;
    settings.Notifications.BaseUrl = ntfyUrl;
    settings.Notifications.Topic = "nhm-p1-local";
    settings.Notifications.AccessToken = string.Empty;
    settings.Notifications.AllowInsecureHttp = true;
    settings.Notifications.RequestTimeoutSeconds = 2;
    settings.Notifications.MaxRetryCount = 1;
    settings.Notifications.InitialRetryDelaySeconds = 1;
    settings.Notifications.NotifyOnDeviceDown = true;
    settings.Notifications.NotifyOnDeviceRecovered = true;
    settings.Notifications.NotifyOnDeviceEscalated = true;
    settings.Notifications.RecoverySuccessThreshold = 1;
    settings.Notifications.EmailEnabled = true;
    settings.Notifications.SmtpHost = "127.0.0.1";
    settings.Notifications.SmtpPort = smtpPort;
    settings.Notifications.SmtpSecurity = SmtpSecurityMode.None;
    settings.Notifications.AllowInsecureSmtp = true;
    settings.Notifications.SenderEmail = "nhm@example.invalid";
    settings.Notifications.SenderDisplayName = "NetworkHealthMonitor";
    settings.Notifications.SmtpConnectionTimeoutSeconds = 2;
    settings.Notifications.EmailMaxRetryCount = 1;
    settings.Notifications.EmailNotifyOnDeviceRecovered = true;
    settings.Notifications.InitialEmailRecipients = [new EmailRecipient { Email = "ops@example.invalid" }];
    settings.Notifications.EscalationEmailRecipients = [new EmailRecipient { Email = "ops@example.invalid" }];
    await settingsService.SaveAsync(settings);
}

static async Task ConfigureBulkUiAcceptanceSettingsAsync()
{
    var settingsService = new AppSettingsService();
    var settings = await settingsService.LoadAsync();
    settings.AcceptanceTest.Enabled = true;
    settings.AcceptanceTest.DeviceNamePrefix = Prefix;
    settings.AutoCheckEnabled = false;
    settings.PingTimeoutMs = 5000;
    settings.MaxParallelPings = 1;
    settings.DefaultFailureThreshold = 3;
    settings.DefaultFailureRetryLimit = 0;
    settings.DefaultFailureRetryIntervalSeconds = 60;
    settings.Notifications.Enabled = false;
    settings.Notifications.EmailEnabled = false;
    await settingsService.SaveAsync(settings);
}

static async Task<Device> SaveDeviceAsync(DeviceService service, string name, string address)
{
    var device = new Device
    {
        Name = name,
        IpAddress = address,
        DeviceType = DeviceType.Server,
        GroupName = GroupName,
        IsActive = true,
        IsEnabled = true,
        AutoCheckEnabled = true,
        CheckIntervalSeconds = 60,
        FailureThreshold = 1,
        FailureRetryLimit = 1,
        FailureRetryIntervalSeconds = 10,
        PingTimeoutMs = 500,
        Description = "P1 runtime acceptance test data"
    };
    var result = await service.SaveAsync(device);
    if (!result.Success)
    {
        throw new InvalidOperationException(result.Message);
    }

    return device;
}

static async Task<Device> SaveBulkUiDeviceAsync(DeviceService service, string name, string address)
{
    var device = new Device
    {
        Name = name,
        IpAddress = address,
        DeviceType = DeviceType.Server,
        GroupName = GroupName,
        IsActive = true,
        IsEnabled = true,
        AutoCheckEnabled = false,
        CheckIntervalSeconds = 60,
        FailureThreshold = 3,
        FailureRetryLimit = 0,
        FailureRetryIntervalSeconds = 60,
        PingTimeoutMs = 5000,
        Description = "P1 bulk UI acceptance test data"
    };
    var result = await service.SaveAsync(device);
    if (!result.Success)
    {
        throw new InvalidOperationException(result.Message);
    }

    return device;
}

static async Task UpdateDeviceAddressDirectAsync(SqliteConnectionFactory connectionFactory, int deviceId, string address)
{
    await using var connection = await connectionFactory.CreateOpenConnectionAsync();
    await using var command = connection.CreateCommand();
    command.CommandText = """
        UPDATE Devices
        SET IpAddress = @IpAddress,
            UpdatedAt = @UpdatedAt
        WHERE Id = @Id;
        """;
    command.Parameters.AddWithValue("@IpAddress", address);
    command.Parameters.AddWithValue("@UpdatedAt", DateTime.Now.ToString("O", CultureInfo.InvariantCulture));
    command.Parameters.AddWithValue("@Id", deviceId);
    var affected = await command.ExecuteNonQueryAsync();
    if (affected != 1)
    {
        throw new InvalidOperationException("Acceptance device address update did not affect exactly one active device.");
    }
}

static async Task UpdateDeviceForRecoveryDirectAsync(
    SqliteConnectionFactory connectionFactory,
    int deviceId,
    string name,
    string address,
    string description)
{
    await using var connection = await connectionFactory.CreateOpenConnectionAsync();
    await using var command = connection.CreateCommand();
    command.CommandText = """
        UPDATE Devices
        SET Name = @Name,
            IpAddress = @IpAddress,
            Description = @Description,
            UpdatedAt = @UpdatedAt
        WHERE Id = @Id;
        """;
    command.Parameters.AddWithValue("@Name", name);
    command.Parameters.AddWithValue("@IpAddress", address);
    command.Parameters.AddWithValue("@Description", description);
    command.Parameters.AddWithValue("@UpdatedAt", DateTime.Now.ToString("O", CultureInfo.InvariantCulture));
    command.Parameters.AddWithValue("@Id", deviceId);
    var affected = await command.ExecuteNonQueryAsync();
    if (affected != 1)
    {
        throw new InvalidOperationException("Acceptance device recovery update did not affect exactly one device.");
    }
}

static async Task<Dictionary<string, object?>> RunP2OperationalChecksAsync(
    SqliteConnectionFactory connectionFactory,
    string artifactsRoot)
{
    var p2 = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase)
    {
        ["mode"] = "p2-ops",
        ["startedAtUtc"] = DateTime.UtcNow.ToString("O", CultureInfo.InvariantCulture),
        ["databasePath"] = DatabasePaths.DatabaseFilePath,
        ["settingsPath"] = DatabasePaths.SettingsFilePath
    };
    var settingsExisted = File.Exists(DatabasePaths.SettingsFilePath);
    var originalSettingsText = settingsExisted
        ? await File.ReadAllTextAsync(DatabasePaths.SettingsFilePath)
        : string.Empty;
    var settingsBackupPath = Path.Combine(artifactsRoot, "settings-before-p2-ops.json");
    await File.WriteAllTextAsync(settingsBackupPath, originalSettingsText);
    p2["settingsBackupPath"] = settingsBackupPath;
    Exception? failure = null;

    try
    {
        await CleanupAcceptanceDataAsync(connectionFactory);

        var maintenance = new DataMaintenanceService(connectionFactory);
        var backupPath = Path.Combine(
            DatabasePaths.BackupDirectory,
            $"NetworkHealthMonitor-{DateTime.Now:yyyyMMdd-HHmmss}.db");
        await maintenance.BackupDatabaseAsync(backupPath);
        await VerifyDatabaseFileAsync(backupPath);
        p2["backup"] = "PASS";
        p2["backupPath"] = backupPath;
        p2["backupSizeBytes"] = new FileInfo(backupPath).Length;

        var repository = new DeviceRepository(connectionFactory);
        var csv = new CsvExportService();
        var importExport = new DeviceImportExportService(csv, repository, maintenance);
        var importPath = Path.Combine(artifactsRoot, "p2-import.csv");
        var exportPath = Path.Combine(artifactsRoot, "p2-devices-export.csv");
        var reportPath = Path.Combine(artifactsRoot, "p2-uptime-report.csv");
        var options = new CsvImportOptions(
            CsvImportMode.Upsert,
            CsvImportScope.AllActiveDevices,
            null,
            string.Empty,
            $"{Prefix}p2-devices.csv",
            Environment.UserName);
        await File.WriteAllTextAsync(
            importPath,
            "Name;IpAddress;DeviceType;GroupName;Description\nNHR-ACCEPTANCE-P2-CSV;192.0.2.201;Server;NHR-ACCEPTANCE-P1;=SUM(1,1)\n",
            new UTF8Encoding(encoderShouldEmitUTF8Identifier: true));
        var preview = await importExport.ReadImportPreviewAsync(importPath, await repository.GetAllAsync(includeDeleted: true), options);
        if (preview.HasBlockingErrors || preview.AddCount != 1)
        {
            throw new InvalidOperationException($"P2 CSV import preview failed. Add={preview.AddCount}, Invalid={preview.InvalidRowCount}, Duplicate={preview.DuplicateCount}.");
        }

        var importResult = await importExport.ApplyImportAsync(preview, options);
        if (importResult.Added != 1)
        {
            throw new InvalidOperationException($"P2 CSV import apply failed. Added={importResult.Added}, Updated={importResult.Updated}, Invalid={importResult.Invalid}.");
        }

        var importedDevice = (await repository.GetAllAsync()).Single(device => device.Name == "NHR-ACCEPTANCE-P2-CSV");
        await importExport.ExportDevicesAsync(new[] { importedDevice }, exportPath, ";");
        var exportedDeviceText = await File.ReadAllTextAsync(exportPath, Encoding.UTF8);
        if (!exportedDeviceText.Contains("NHR-ACCEPTANCE-P2-CSV", StringComparison.Ordinal)
            || !exportedDeviceText.Contains("'=SUM(1,1)", StringComparison.Ordinal))
        {
            throw new InvalidOperationException("P2 CSV export did not include expected row or spreadsheet formula escaping.");
        }

        p2["csv"] = "PASS";
        p2["csvImportAdded"] = importResult.Added;
        p2["csvExportPath"] = exportPath;

        var pingLogRepository = new PingLogRepository(connectionFactory);
        await pingLogRepository.AddRangeAsync(new[]
        {
            new PingLog
            {
                DeviceId = importedDevice.Id,
                DeviceName = importedDevice.Name,
                IpAddress = importedDevice.IpAddress,
                DeviceType = importedDevice.DeviceType,
                GroupName = importedDevice.GroupName,
                Status = DeviceStatus.Online,
                IsReachable = true,
                LatencyMs = 2,
                ResponseMessage = "Pong",
                CheckedAt = DateTime.Now.AddMinutes(-2),
                Source = "P2Runtime",
                TriggerType = PingTriggerType.Manual,
                WorkerInstanceId = "p2-runtime"
            },
            new PingLog
            {
                DeviceId = importedDevice.Id,
                DeviceName = importedDevice.Name,
                IpAddress = importedDevice.IpAddress,
                DeviceType = importedDevice.DeviceType,
                GroupName = importedDevice.GroupName,
                Status = DeviceStatus.Offline,
                IsReachable = false,
                ErrorCode = "Timeout",
                ErrorMessage = "P2 runtime timeout sample",
                CheckedAt = DateTime.Now.AddMinutes(-1),
                Source = "P2Runtime",
                TriggerType = PingTriggerType.Manual,
                WorkerInstanceId = "p2-runtime"
            }
        });
        var uptimeReport = await pingLogRepository.GetUptimeReportAsync(new[] { importedDevice.Id });
        var uptimeItem = uptimeReport.Single();
        if (uptimeItem.TotalChecksOverall != 2 || uptimeItem.SuccessfulChecksOverall != 1 || uptimeItem.FailedChecksOverall != 1)
        {
            throw new InvalidOperationException("P2 uptime report did not read expected SQLite ping data.");
        }

        await csv.ExportUptimeReportAsync(uptimeReport, reportPath, ";");
        p2["uptimeReport"] = "PASS";
        p2["uptimeReportPath"] = reportPath;

        var settingsService = new AppSettingsService();
        var settings = await settingsService.LoadAsync();
        settings.PingTimeoutMs = Math.Clamp(settings.PingTimeoutMs + 1, AppSettings.MinPingTimeoutMs, AppSettings.MaxPingTimeoutMs);
        settings.MaxParallelPings = Math.Clamp(settings.MaxParallelPings, AppSettings.MinParallelPings, AppSettings.MaxParallelPingsLimit);
        settings.DefaultFailureRetryIntervalSeconds = Math.Clamp(settings.DefaultFailureRetryIntervalSeconds, AppSettings.MinFailureRetryIntervalSeconds, AppSettings.MaxFailureRetryIntervalSeconds);
        settings.OpenUiOnWindowsLogin = false;
        await settingsService.SaveAsync(settings);
        var reloaded = await settingsService.LoadAsync();
        if (reloaded.PingTimeoutMs != settings.PingTimeoutMs || reloaded.OpenUiOnWindowsLogin)
        {
            throw new InvalidOperationException("P2 settings persistence round-trip failed.");
        }

        p2["settingsPersistence"] = "PASS";
        p2["overall"] = "PASS";
    }
    catch (Exception ex)
    {
        failure = ex;
        p2["overall"] = "FAIL";
        p2["error"] = ex.Message;
    }
    finally
    {
        if (settingsExisted)
        {
            await File.WriteAllTextAsync(DatabasePaths.SettingsFilePath, originalSettingsText);
            p2["settingsRestored"] = "PASS";
        }
        else if (File.Exists(DatabasePaths.SettingsFilePath))
        {
            File.Delete(DatabasePaths.SettingsFilePath);
            p2["settingsRestored"] = "PASS";
        }

        await CleanupAcceptanceDataAsync(connectionFactory);
        p2["cleanupSummary"] = await BuildAcceptanceSummaryAsync(connectionFactory);
        p2["finishedAtUtc"] = DateTime.UtcNow.ToString("O", CultureInfo.InvariantCulture);
        await File.WriteAllTextAsync(
            Path.Combine(artifactsRoot, "p2-ops-report.json"),
            JsonSerializer.Serialize(p2, new JsonSerializerOptions { WriteIndented = true }));
    }

    if (failure is not null)
    {
        throw failure;
    }

    return p2;
}

static async Task CleanupAcceptanceDataAsync(SqliteConnectionFactory connectionFactory)
{
    await using var connection = await connectionFactory.CreateOpenConnectionAsync();
    await using var transaction = await connection.BeginTransactionAsync();
    var commands = new[]
    {
        "DELETE FROM NotificationOutbox WHERE IdempotencyKey LIKE 'nhr-acceptance-%' OR DeviceId IN (SELECT Id FROM Devices WHERE Name LIKE 'NHR-ACCEPTANCE-%');",
        "DELETE FROM CsvImportAudits WHERE FileName LIKE 'NHR-ACCEPTANCE-%';",
        "DELETE FROM DeviceIncidents WHERE DeviceId IN (SELECT Id FROM Devices WHERE Name LIKE 'NHR-ACCEPTANCE-%');",
        "DELETE FROM Outages WHERE DeviceId IN (SELECT Id FROM Devices WHERE Name LIKE 'NHR-ACCEPTANCE-%');",
        "DELETE FROM PingLogs WHERE DeviceId IN (SELECT Id FROM Devices WHERE Name LIKE 'NHR-ACCEPTANCE-%') OR DeviceName LIKE 'NHR-ACCEPTANCE-%' OR SchedulePlanName LIKE 'NHR-ACCEPTANCE-%';",
        "DELETE FROM SchedulePlans WHERE Name LIKE 'NHR-ACCEPTANCE-%';",
        "DELETE FROM DeviceGroupMembers WHERE DeviceId IN (SELECT Id FROM Devices WHERE Name LIKE 'NHR-ACCEPTANCE-%') OR GroupId IN (SELECT Id FROM DeviceGroups WHERE Name = 'NHR-ACCEPTANCE-P1');",
        "DELETE FROM Devices WHERE Name LIKE 'NHR-ACCEPTANCE-%';",
        "DELETE FROM DeviceGroups WHERE Name = 'NHR-ACCEPTANCE-P1';"
    };

    foreach (var sql in commands)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = (SqliteTransaction)transaction;
        command.CommandText = sql;
        await command.ExecuteNonQueryAsync();
    }

    await transaction.CommitAsync();
}

static async Task<int> CountLogsForAcceptanceDevicesAsync(SqliteConnectionFactory connectionFactory)
{
    return await CountAsync(connectionFactory, "SELECT COUNT(1) FROM PingLogs WHERE DeviceName LIKE 'NHR-ACCEPTANCE-%';");
}

static async Task<Dictionary<string, object?>> BuildAcceptanceSummaryAsync(SqliteConnectionFactory connectionFactory)
{
    var summary = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase)
    {
        ["generatedAtUtc"] = DateTime.UtcNow.ToString("O", CultureInfo.InvariantCulture),
        ["acceptanceDeviceCount"] = await CountAsync(connectionFactory, "SELECT COUNT(1) FROM Devices WHERE Name LIKE 'NHR-ACCEPTANCE-%';"),
        ["acceptancePingLogCount"] = await CountLogsForAcceptanceDevicesAsync(connectionFactory),
        ["acceptanceIncidentCount"] = await CountAsync(connectionFactory, "SELECT COUNT(1) FROM DeviceIncidents WHERE DeviceId IN (SELECT Id FROM Devices WHERE Name LIKE 'NHR-ACCEPTANCE-%');"),
        ["acceptanceOutboxCount"] = await CountOutboxForAcceptanceAsync(connectionFactory),
        ["acceptanceSchedulePlanCount"] = await CountAsync(connectionFactory, "SELECT COUNT(1) FROM SchedulePlans WHERE Name LIKE 'NHR-ACCEPTANCE-%';"),
        ["openIncidentDuplicateCount"] = await CountAsync(connectionFactory, """
            SELECT COUNT(1)
            FROM (
                SELECT DeviceId
                FROM DeviceIncidents
                WHERE Status = 'Open'
                GROUP BY DeviceId
                HAVING COUNT(1) > 1
            );
            """),
        ["outboxIdempotencyDuplicateCount"] = await CountAsync(connectionFactory, """
            SELECT COUNT(1)
            FROM (
                SELECT IdempotencyKey
                FROM NotificationOutbox
                WHERE IdempotencyKey IS NOT NULL
                  AND trim(IdempotencyKey) <> ''
                GROUP BY IdempotencyKey
                HAVING COUNT(1) > 1
            );
            """)
    };

    await using var connection = await connectionFactory.CreateOpenConnectionAsync();
    await using var command = connection.CreateCommand();
    command.CommandText = """
        SELECT d.Id,
               d.Name,
               d.LastStatus,
               d.LastLatencyMs,
               d.LastCheckedAt,
               COUNT(p.Id) AS PingCount,
               COALESCE(MAX(p.CheckedAt), '') AS LastPingAt
        FROM Devices d
        LEFT JOIN PingLogs p ON p.DeviceId = d.Id
        WHERE d.Name LIKE 'NHR-ACCEPTANCE-%'
        GROUP BY d.Id, d.Name, d.LastStatus, d.LastLatencyMs, d.LastCheckedAt
        ORDER BY d.Name;
        """;

    var devices = new List<Dictionary<string, object?>>();
    await using var reader = await command.ExecuteReaderAsync();
    while (await reader.ReadAsync())
    {
        devices.Add(new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase)
        {
            ["id"] = reader.GetInt64(0),
            ["name"] = reader.GetString(1),
            ["lastStatus"] = reader.IsDBNull(2) ? null : reader.GetString(2),
            ["lastLatencyMs"] = reader.IsDBNull(3) ? null : reader.GetInt32(3),
            ["lastCheckedAt"] = reader.IsDBNull(4) ? null : reader.GetString(4),
            ["pingCount"] = reader.GetInt64(5),
            ["lastPingAt"] = reader.GetString(6)
        });
    }

    summary["devices"] = devices;
    return summary;
}

static async Task<int> CountOpenIncidentsAsync(SqliteConnectionFactory connectionFactory, int deviceId)
{
    return await CountAsync(connectionFactory, "SELECT COUNT(1) FROM DeviceIncidents WHERE DeviceId = @DeviceId AND Status = 'Open';", ("@DeviceId", deviceId));
}

static async Task<int> CountClosedIncidentsAsync(SqliteConnectionFactory connectionFactory, int deviceId)
{
    return await CountAsync(connectionFactory, "SELECT COUNT(1) FROM DeviceIncidents WHERE DeviceId = @DeviceId AND Status = 'Closed';", ("@DeviceId", deviceId));
}

static async Task<Dictionary<string, object?>> BuildIncidentSnapshotAsync(SqliteConnectionFactory connectionFactory, int deviceId)
{
    await using var connection = await connectionFactory.CreateOpenConnectionAsync();
    await using var command = connection.CreateCommand();
    command.CommandText = """
        SELECT Id,
               Status,
               CurrentFailureCount,
               RecoverySuccessCount,
               COALESCE(LastFailureAtUtc, ''),
               COALESCE(LastSuccessAtUtc, ''),
               COALESCE(LastObservedAtUtc, ''),
               COALESCE(UpdatedAtUtc, '')
        FROM DeviceIncidents
        WHERE DeviceId = @DeviceId
        ORDER BY Id DESC
        LIMIT 1;
        """;
    command.Parameters.AddWithValue("@DeviceId", deviceId);
    await using var reader = await command.ExecuteReaderAsync();
    if (!await reader.ReadAsync())
    {
        return new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
    }

    return new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase)
    {
        ["id"] = reader.GetInt64(0),
        ["status"] = reader.GetString(1),
        ["currentFailureCount"] = reader.GetInt32(2),
        ["recoverySuccessCount"] = reader.GetInt32(3),
        ["lastFailureAtUtc"] = reader.GetString(4),
        ["lastSuccessAtUtc"] = reader.GetString(5),
        ["lastObservedAtUtc"] = reader.GetString(6),
        ["updatedAtUtc"] = reader.GetString(7)
    };
}

static async Task<IReadOnlyList<Dictionary<string, object?>>> BuildRecentPingLogSnapshotAsync(SqliteConnectionFactory connectionFactory, int deviceId)
{
    await using var connection = await connectionFactory.CreateOpenConnectionAsync();
    await using var command = connection.CreateCommand();
    command.CommandText = """
        SELECT Status,
               IsReachable,
               IpAddress,
               COALESCE(ErrorCode, ''),
               COALESCE(ErrorMessage, ''),
               COALESCE(SchedulePlanName, ''),
               CheckedAt
        FROM PingLogs
        WHERE DeviceId = @DeviceId
        ORDER BY Id DESC
        LIMIT 8;
        """;
    command.Parameters.AddWithValue("@DeviceId", deviceId);
    await using var reader = await command.ExecuteReaderAsync();
    var logs = new List<Dictionary<string, object?>>();
    while (await reader.ReadAsync())
    {
        logs.Add(new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase)
        {
            ["status"] = reader.GetString(0),
            ["isReachable"] = reader.GetBoolean(1),
            ["ipAddress"] = reader.GetString(2),
            ["errorCode"] = reader.GetString(3),
            ["errorMessage"] = reader.GetString(4),
            ["schedulePlanName"] = reader.GetString(5),
            ["checkedAt"] = reader.GetString(6)
        });
    }

    return logs;
}

static async Task<int> CountOutboxForAcceptanceAsync(SqliteConnectionFactory connectionFactory)
{
    return await CountAsync(connectionFactory, "SELECT COUNT(1) FROM NotificationOutbox WHERE IdempotencyKey LIKE 'nhr-acceptance-%' OR DeviceId IN (SELECT Id FROM Devices WHERE Name LIKE 'NHR-ACCEPTANCE-%');");
}

static async Task<int> CountSentOutboxForAcceptanceAsync(SqliteConnectionFactory connectionFactory)
{
    return await CountAsync(connectionFactory, "SELECT COUNT(1) FROM NotificationOutbox WHERE Status = 'Sent' AND (IdempotencyKey LIKE 'nhr-acceptance-%' OR DeviceId IN (SELECT Id FROM Devices WHERE Name LIKE 'NHR-ACCEPTANCE-%'));");
}

static async Task<int> CountPendingOutboxForAcceptanceAsync(SqliteConnectionFactory connectionFactory)
{
    return await CountAsync(connectionFactory, "SELECT COUNT(1) FROM NotificationOutbox WHERE Status IN ('Pending','Processing') AND IdempotencyKey LIKE 'nhr-acceptance-%';");
}

static async Task VerifyDatabaseFileAsync(string databasePath)
{
    var connectionString = new SqliteConnectionStringBuilder
    {
        DataSource = databasePath,
        Mode = SqliteOpenMode.ReadOnly,
        Pooling = false
    }.ToString();

    await using var connection = new SqliteConnection(connectionString);
    await connection.OpenAsync();
    await DatabaseSchemaContract.VerifyAsync(connection);
}

static async Task<string> ReadDeviceStatusAsync(SqliteConnectionFactory connectionFactory, int deviceId)
{
    return await ScalarStringAsync(connectionFactory, "SELECT LastStatus FROM Devices WHERE Id = @DeviceId;", ("@DeviceId", deviceId));
}

static async Task<int> CountDuplicateOpenIncidentsAsync(SqliteConnectionFactory connectionFactory)
{
    return await CountAsync(connectionFactory, """
        SELECT COUNT(1)
        FROM (
            SELECT DeviceId
            FROM DeviceIncidents
            WHERE Status = 'Open'
            GROUP BY DeviceId
            HAVING COUNT(1) > 1
        );
        """);
}

static async Task<int> CountDuplicateOutboxIdempotencyAsync(SqliteConnectionFactory connectionFactory)
{
    return await CountAsync(connectionFactory, """
        SELECT COUNT(1)
        FROM (
            SELECT IdempotencyKey
            FROM NotificationOutbox
            WHERE IdempotencyKey IS NOT NULL
              AND trim(IdempotencyKey) <> ''
            GROUP BY IdempotencyKey
            HAVING COUNT(1) > 1
        );
        """);
}

static async Task<int> CountAsync(SqliteConnectionFactory connectionFactory, string sql, params (string Name, object Value)[] parameters)
{
    await using var connection = await connectionFactory.CreateOpenConnectionAsync();
    await using var command = connection.CreateCommand();
    command.CommandText = sql;
    foreach (var parameter in parameters)
    {
        command.Parameters.AddWithValue(parameter.Name, parameter.Value);
    }

    return Convert.ToInt32(await command.ExecuteScalarAsync(), CultureInfo.InvariantCulture);
}

static async Task<string> ScalarStringAsync(SqliteConnectionFactory connectionFactory, string sql, params (string Name, object Value)[] parameters)
{
    await using var connection = await connectionFactory.CreateOpenConnectionAsync();
    await using var command = connection.CreateCommand();
    command.CommandText = sql;
    foreach (var parameter in parameters)
    {
        command.Parameters.AddWithValue(parameter.Name, parameter.Value);
    }

    return Convert.ToString(await command.ExecuteScalarAsync(), CultureInfo.InvariantCulture) ?? string.Empty;
}

static async Task<int> ReadHeartbeatAgeSecondsAsync(SqliteConnectionFactory connectionFactory)
{
    var heartbeat = await new WorkerHeartbeatRepository(connectionFactory).GetLatestAsync();
    return heartbeat is null ? int.MaxValue : Math.Max(0, (int)(DateTime.UtcNow - heartbeat.LastSeenAtUtc).TotalSeconds);
}

static string ResolveWorkerExecutablePath(string? overridePath)
{
    if (!string.IsNullOrWhiteSpace(overridePath))
    {
        var fullPath = Path.GetFullPath(overridePath);
        return File.Exists(fullPath)
            ? fullPath
            : throw new FileNotFoundException("Worker executable was not found.", fullPath);
    }

    var candidates = new[]
    {
        Path.GetFullPath(Path.Combine(Environment.CurrentDirectory, "artifacts", "win-x64", "Worker", "NetworkHealthMonitor.Worker.exe")),
        Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "artifacts", "win-x64", "Worker", "NetworkHealthMonitor.Worker.exe"))
    };

    return candidates.FirstOrDefault(File.Exists)
        ?? throw new FileNotFoundException("Worker executable was not found. Pass --worker-exe=<path>.");
}

static Process StartConsoleWorker(string workerExecutablePath, string dataRoot)
{
    if (!File.Exists(workerExecutablePath))
    {
        throw new FileNotFoundException("Worker executable was not found.", workerExecutablePath);
    }

    var process = new Process
    {
        StartInfo = new ProcessStartInfo
        {
            FileName = workerExecutablePath,
            WorkingDirectory = Path.GetDirectoryName(workerExecutablePath) ?? Environment.CurrentDirectory,
            UseShellExecute = false,
            CreateNoWindow = true
        }
    };
    process.StartInfo.ArgumentList.Add("--data-dir");
    process.StartInfo.ArgumentList.Add(dataRoot);
    process.StartInfo.ArgumentList.Add("--poll-seconds");
    process.StartInfo.ArgumentList.Add("2");

    return process.Start()
        ? process
        : throw new InvalidOperationException("Console Worker process could not be started.");
}

static async Task<int> RunDuplicateConsoleWorkerAsync(string workerExecutablePath, string dataRoot)
{
    using var duplicate = StartConsoleWorker(workerExecutablePath, dataRoot);
    try
    {
        if (!await WaitForProcessExitAsync(duplicate, TimeSpan.FromSeconds(15)))
        {
            throw new TimeoutException("Duplicate Worker process did not exit within 15 seconds.");
        }

        return duplicate.ExitCode;
    }
    finally
    {
        if (!duplicate.HasExited)
        {
            duplicate.Kill(entireProcessTree: true);
            await duplicate.WaitForExitAsync();
        }
    }
}

static async Task StopConsoleWorkerAsync(Process? process)
{
    if (process is null)
    {
        return;
    }

    try
    {
        if (!process.HasExited)
        {
            process.Kill(entireProcessTree: true);
            await process.WaitForExitAsync();
        }
    }
    catch (InvalidOperationException)
    {
    }
    finally
    {
        process.Dispose();
    }
}

static async Task<ServiceState> ReadServiceStateAsync()
{
    var output = await RunProcessAsync("powershell.exe", $"-NoProfile -Command \"Get-CimInstance Win32_Service -Filter \\\"Name='{ServiceName}'\\\" | ConvertTo-Json -Compress\"");
    using var document = JsonDocument.Parse(output.Output);
    var root = document.RootElement;
    return new ServiceState(
        root.GetProperty("State").GetString() ?? string.Empty,
        root.GetProperty("StartMode").GetString() ?? string.Empty,
        root.TryGetProperty("ProcessId", out var processId) ? processId.GetInt32() : 0);
}

static async Task StopWorkerAsync()
{
    await RunProcessAsync("sc.exe", $"stop \"{ServiceName}\"");
    await WaitForServiceStatusAsync("STOPPED", TimeSpan.FromSeconds(30));
}

static async Task StartWorkerAsync()
{
    await RunProcessAsync("sc.exe", $"start \"{ServiceName}\"");
    await WaitForServiceStatusAsync("RUNNING", TimeSpan.FromSeconds(30));
}

static async Task WaitForServiceStatusAsync(string status, TimeSpan timeout)
{
    await WaitUntilAsync(
        async () =>
        {
            var output = await RunProcessAsync("sc.exe", $"query \"{ServiceName}\"");
            return output.Output.Contains($"STATE", StringComparison.OrdinalIgnoreCase)
                && output.Output.Contains(status, StringComparison.OrdinalIgnoreCase);
        },
        timeout,
        $"Service did not reach {status}.");
}

static async Task<int> CountWorkerProcessesAsync()
{
    var output = await RunProcessAsync("powershell.exe", "-NoProfile -Command \"@(Get-Process -Name NetworkHealthMonitor.Worker -ErrorAction SilentlyContinue).Count\"");
    return int.TryParse(output.Output.Trim(), out var count) ? count : -1;
}

static async Task<int> CountConsoleWorkerProcessesAsync()
{
    var output = await RunProcessAsync("powershell.exe", "-NoProfile -Command \"@(Get-CimInstance Win32_Process -Filter \\\"Name='NetworkHealthMonitor.Worker.exe'\\\" | Where-Object { $_.CommandLine -match '--run-once|--health-check|--database|--data-dir' }).Count\"");
    return int.TryParse(output.Output.Trim(), out var count) ? count : -1;
}

static bool IsAccessDenied(string message)
{
    return message.Contains("Access is denied", StringComparison.OrdinalIgnoreCase)
        || message.Contains("Erisim engellendi", StringComparison.OrdinalIgnoreCase)
        || message.Contains("Eri\uFFFD", StringComparison.OrdinalIgnoreCase)
        || message.Contains("FAILED 5", StringComparison.OrdinalIgnoreCase);
}

static async Task<(int ExitCode, string Output, string Error)> RunProcessAsync(string fileName, string arguments)
{
    using var process = new Process();
    process.StartInfo = new ProcessStartInfo
    {
        FileName = fileName,
        Arguments = arguments,
        CreateNoWindow = true,
        UseShellExecute = false,
        RedirectStandardOutput = true,
        RedirectStandardError = true
    };
    process.Start();
    var outputTask = process.StandardOutput.ReadToEndAsync();
    var errorTask = process.StandardError.ReadToEndAsync();
    await process.WaitForExitAsync();
    var result = (ExitCode: process.ExitCode, Output: await outputTask, Error: await errorTask);
    if (result.ExitCode != 0)
    {
        throw new InvalidOperationException($"{fileName} {arguments} failed with {result.ExitCode}: {result.Error} {result.Output}");
    }

    return result;
}

static async Task WaitUntilAsync(Func<Task<bool>> condition, TimeSpan timeout, string failureMessage)
{
    if (await WaitUntilOrFalseAsync(condition, timeout))
    {
        return;
    }

    throw new TimeoutException(failureMessage);
}

static async Task<bool> WaitUntilOrFalseAsync(Func<Task<bool>> condition, TimeSpan timeout)
{
    var stopAt = DateTime.UtcNow.Add(timeout);
    while (DateTime.UtcNow < stopAt)
    {
        if (await condition())
        {
            return true;
        }

        await Task.Delay(TimeSpan.FromSeconds(2));
    }

    return false;
}

static async Task<bool> WaitForProcessExitAsync(Process process, TimeSpan timeout)
{
    var waitTask = process.WaitForExitAsync();
    return await Task.WhenAny(waitTask, Task.Delay(timeout)) == waitTask;
}

public sealed record ServiceState(string Status, string StartMode, int ProcessId);

public sealed class FakeHttpServer : IAsyncDisposable
{
    private readonly TcpListener _listener;
    private readonly CancellationTokenSource _cts = new();
    private readonly Task _loop;

    private FakeHttpServer(TcpListener listener)
    {
        _listener = listener;
        Port = ((IPEndPoint)_listener.LocalEndpoint).Port;
        Url = $"http://127.0.0.1:{Port}";
        _loop = Task.Run(AcceptLoopAsync);
    }

    public int Port { get; }

    public string Url { get; }

    public int RequestCount { get; private set; }

    public static Task<FakeHttpServer> StartAsync()
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        return Task.FromResult(new FakeHttpServer(listener));
    }

    public async ValueTask DisposeAsync()
    {
        _cts.Cancel();
        _listener.Stop();
        try { await _loop; } catch { }
        _cts.Dispose();
    }

    private async Task AcceptLoopAsync()
    {
        while (!_cts.IsCancellationRequested)
        {
            try
            {
                var client = await _listener.AcceptTcpClientAsync(_cts.Token);
                _ = Task.Run(() => HandleClientAsync(client), _cts.Token);
            }
            catch when (_cts.IsCancellationRequested)
            {
                return;
            }
        }
    }

    private async Task HandleClientAsync(TcpClient client)
    {
        using (client)
        await using (var stream = client.GetStream())
        using (var reader = new StreamReader(stream, Encoding.UTF8, leaveOpen: true))
        {
            string? line;
            var contentLength = 0;
            while (!string.IsNullOrEmpty(line = await reader.ReadLineAsync()))
            {
                if (line.StartsWith("Content-Length:", StringComparison.OrdinalIgnoreCase)
                    && int.TryParse(line.Split(':', 2)[1].Trim(), out var length))
                {
                    contentLength = length;
                }
            }

            if (contentLength > 0)
            {
                var buffer = new char[contentLength];
                await reader.ReadBlockAsync(buffer);
            }

            RequestCount++;
            var response = Encoding.UTF8.GetBytes("HTTP/1.1 200 OK\r\nContent-Length: 2\r\nConnection: close\r\n\r\nOK");
            await stream.WriteAsync(response);
        }
    }
}

public sealed class FakeSmtpServer : IAsyncDisposable
{
    private readonly TcpListener _listener;
    private readonly CancellationTokenSource _cts = new();
    private readonly Task _loop;

    private FakeSmtpServer(TcpListener listener)
    {
        _listener = listener;
        Port = ((IPEndPoint)_listener.LocalEndpoint).Port;
        _loop = Task.Run(AcceptLoopAsync);
    }

    public int Port { get; }

    public int MessageCount { get; private set; }

    public static Task<FakeSmtpServer> StartAsync()
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        return Task.FromResult(new FakeSmtpServer(listener));
    }

    public async ValueTask DisposeAsync()
    {
        _cts.Cancel();
        _listener.Stop();
        try { await _loop; } catch { }
        _cts.Dispose();
    }

    private async Task AcceptLoopAsync()
    {
        while (!_cts.IsCancellationRequested)
        {
            try
            {
                var client = await _listener.AcceptTcpClientAsync(_cts.Token);
                _ = Task.Run(() => HandleClientAsync(client), _cts.Token);
            }
            catch when (_cts.IsCancellationRequested)
            {
                return;
            }
        }
    }

    private async Task HandleClientAsync(TcpClient client)
    {
        using (client)
        await using (var stream = client.GetStream())
        using (var reader = new StreamReader(stream, Encoding.ASCII, leaveOpen: true))
        await using (var writer = new StreamWriter(stream, Encoding.ASCII, leaveOpen: true) { NewLine = "\r\n", AutoFlush = true })
        {
            await writer.WriteLineAsync("220 nhm-p1.local ESMTP");
            var inData = false;
            string? line;
            while ((line = await reader.ReadLineAsync()) is not null)
            {
                if (inData)
                {
                    if (line == ".")
                    {
                        MessageCount++;
                        inData = false;
                        await writer.WriteLineAsync("250 OK");
                    }

                    continue;
                }

                if (line.StartsWith("EHLO", StringComparison.OrdinalIgnoreCase)
                    || line.StartsWith("HELO", StringComparison.OrdinalIgnoreCase))
                {
                    await writer.WriteLineAsync("250-nhm-p1.local");
                    await writer.WriteLineAsync("250 OK");
                }
                else if (line.StartsWith("DATA", StringComparison.OrdinalIgnoreCase))
                {
                    inData = true;
                    await writer.WriteLineAsync("354 End data with <CR><LF>.<CR><LF>");
                }
                else if (line.StartsWith("QUIT", StringComparison.OrdinalIgnoreCase))
                {
                    await writer.WriteLineAsync("221 Bye");
                    return;
                }
                else
                {
                    await writer.WriteLineAsync("250 OK");
                }
            }
        }
    }
}
