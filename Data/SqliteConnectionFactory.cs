using System.IO;
using System.Globalization;
using Microsoft.Data.Sqlite;
using NetworkHealthMonitor.Infrastructure;
using NetworkHealthMonitor.Models;

namespace NetworkHealthMonitor.Data;

public sealed class SqliteConnectionFactory : IDatabaseInitializer
{
    private readonly string _connectionString;
    private readonly IDatabaseMigrationRunner _migrationRunner;

    public SqliteConnectionFactory()
        : this(new DatabaseMigrationRunner())
    {
    }

    public SqliteConnectionFactory(IDatabaseMigrationRunner migrationRunner)
    {
        _migrationRunner = migrationRunner;
        DatabasePaths.EnsureDirectories();
        _connectionString = new SqliteConnectionStringBuilder
        {
            DataSource = DatabasePaths.DatabaseFilePath,
            ForeignKeys = true,
            Pooling = true
        }.ToString();
    }

    public async Task<SqliteConnection> CreateOpenConnectionAsync()
    {
        var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync();

        await ExecuteAsync(connection, "PRAGMA foreign_keys = ON;");
        await ExecuteAsync(connection, $"PRAGMA busy_timeout = {AppSettings.SqliteBusyTimeoutMs};");
        return connection;
    }

    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        using var initializationLock = AcquireInitializationLock();

        var migrationResult = await LegacyDataMigrationService.MigrateIfNeededAsync();
        if (!migrationResult.Success)
        {
            AppErrorLogger.LogInfo($"Legacy data migration skipped: {migrationResult.Message}");
        }

        await using var connection = await CreateOpenConnectionAsync();
        await ExecuteAsync(connection, "PRAGMA journal_mode = WAL;");
        await ExecuteAsync(connection, "PRAGMA synchronous = NORMAL;");

        await CreateSchemaMigrationsAsync(connection);
        await CreateSchedulePlansAsync(connection);
        await CreateDeviceGroupsAsync(connection);
        await CreateDevicesAsync(connection);
        await CreateDeviceGroupMembersAsync(connection);
        await CreatePingLogsAsync(connection);
        await CreateOutagesAsync(connection);
        await CreateDeviceIncidentsAsync(connection);
        await CreateNotificationOutboxAsync(connection);
        await CreateCsvImportAuditsAsync(connection);
        await CreateWorkerHeartbeatAsync(connection);
        await CreateAvailabilitySchemaAsync(connection);
        await CreateAppSettingsAsync(connection);
        await RecordMigrationAsync(connection, "2026071501-core-server-schema");
        await _migrationRunner.ApplyMigrationsAsync(connection, cancellationToken);
        await CreateIndexesAsync(connection);
        await MigrateLegacyGroupNamesAsync(connection);
        await DatabaseSchemaContract.VerifyAsync(connection, cancellationToken);
        LogInitializationMetadata();
    }

    public async Task VerifySchemaAsync(CancellationToken cancellationToken = default)
    {
        await using var connection = await CreateOpenConnectionAsync();
        await DatabaseSchemaContract.VerifyAsync(connection, cancellationToken);
    }

    private static void LogInitializationMetadata()
    {
        var build = ApplicationBuildInfo.Current;
        AppErrorLogger.LogInfo(
            $"Database initialized. ProductVersion={build.ProductVersion}; FileVersion={build.FileVersion}; BuildTimestampUtc={build.BuildTimestampUtc}; GitCommitSha={build.GitCommitSha}; ExpectedSchemaVersion={build.ExpectedSchemaVersion}; DatabasePath={DatabasePaths.DatabaseFilePath}");
    }

    private static IDisposable AcquireInitializationLock()
    {
        var mutex = new Mutex(false, @"Global\NetworkHealthMonitorDatabaseInitialization");
        if (!mutex.WaitOne(TimeSpan.FromSeconds(60)))
        {
            mutex.Dispose();
            throw new TimeoutException("Veritabanı başlatma kilidi zaman aşımına uğradı.");
        }

        return new MutexLease(mutex);
    }

    private sealed class MutexLease : IDisposable
    {
        private readonly Mutex _mutex;
        private bool _disposed;

        public MutexLease(Mutex mutex)
        {
            _mutex = mutex;
        }

        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            _mutex.ReleaseMutex();
            _mutex.Dispose();
            _disposed = true;
        }
    }

    public async Task CheckpointAsync()
    {
        await using var connection = await CreateOpenConnectionAsync();
        await ExecuteAsync(connection, "PRAGMA wal_checkpoint(FULL);");
    }

    private static async Task CreateSchemaMigrationsAsync(SqliteConnection connection)
    {
        await ExecuteAsync(connection, """
            CREATE TABLE IF NOT EXISTS SchemaMigrations (
                Version TEXT PRIMARY KEY,
                AppliedAtUtc TEXT NOT NULL
            );
            """);
    }

    private static async Task RecordMigrationAsync(SqliteConnection connection, string version)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT OR IGNORE INTO SchemaMigrations (Version, AppliedAtUtc)
            VALUES (@Version, @AppliedAtUtc);
            """;
        command.Parameters.AddWithValue("@Version", version);
        command.Parameters.AddWithValue("@AppliedAtUtc", DateTime.UtcNow.ToString("O"));
        await command.ExecuteNonQueryAsync();
    }

    private static async Task CreateSchedulePlansAsync(SqliteConnection connection)
    {
        await ExecuteAsync(connection, $"""
            CREATE TABLE IF NOT EXISTS SchedulePlans (
                Id INTEGER PRIMARY KEY AUTOINCREMENT,
                Name TEXT NOT NULL,
                TargetType TEXT NOT NULL,
                TargetValue TEXT NOT NULL DEFAULT '',
                IntervalMinutes INTEGER NOT NULL DEFAULT {AppSettings.DefaultSchedulePlanIntervalMinutes},
                TimeoutMs INTEGER NOT NULL DEFAULT {AppSettings.DefaultPingTimeoutMs},
                MaxParallelism INTEGER NOT NULL DEFAULT {AppSettings.DefaultSchedulePlanMaxParallelism},
                FailureThreshold INTEGER NOT NULL DEFAULT {AppSettings.DefaultFailureThresholdValue},
                IsActive INTEGER NOT NULL DEFAULT 1,
                Description TEXT NOT NULL DEFAULT '',
                LastRunAt TEXT NULL,
                NextRunAt TEXT NULL,
                LastStatus TEXT NOT NULL DEFAULT '',
                CreatedAt TEXT NOT NULL,
                UpdatedAt TEXT NOT NULL
            );
            """);

        await EnsureColumnAsync(connection, "SchedulePlans", "TargetValue", "TEXT NOT NULL DEFAULT ''");
        await EnsureColumnAsync(connection, "SchedulePlans", "IntervalMinutes", $"INTEGER NOT NULL DEFAULT {AppSettings.DefaultSchedulePlanIntervalMinutes}");
        await EnsureColumnAsync(connection, "SchedulePlans", "TimeoutMs", $"INTEGER NOT NULL DEFAULT {AppSettings.DefaultPingTimeoutMs}");
        await EnsureColumnAsync(connection, "SchedulePlans", "MaxParallelism", $"INTEGER NOT NULL DEFAULT {AppSettings.DefaultSchedulePlanMaxParallelism}");
        await EnsureColumnAsync(connection, "SchedulePlans", "FailureThreshold", $"INTEGER NOT NULL DEFAULT {AppSettings.DefaultFailureThresholdValue}");
        await EnsureColumnAsync(connection, "SchedulePlans", "IsActive", "INTEGER NOT NULL DEFAULT 1");
        await EnsureColumnAsync(connection, "SchedulePlans", "Description", "TEXT NOT NULL DEFAULT ''");
        await EnsureColumnAsync(connection, "SchedulePlans", "LastRunAt", "TEXT NULL");
        await EnsureColumnAsync(connection, "SchedulePlans", "NextRunAt", "TEXT NULL");
        await EnsureColumnAsync(connection, "SchedulePlans", "LastStatus", "TEXT NOT NULL DEFAULT ''");
        await EnsureColumnAsync(connection, "SchedulePlans", "CreatedAt", $"TEXT NOT NULL DEFAULT '{DateTime.Now:O}'");
        await EnsureColumnAsync(connection, "SchedulePlans", "UpdatedAt", $"TEXT NOT NULL DEFAULT '{DateTime.Now:O}'");
    }

    private static async Task CreateDeviceGroupsAsync(SqliteConnection connection)
    {
        await ExecuteAsync(connection, """
            CREATE TABLE IF NOT EXISTS DeviceGroups (
                Id INTEGER PRIMARY KEY AUTOINCREMENT,
                Name TEXT NOT NULL UNIQUE,
                Description TEXT NOT NULL DEFAULT '',
                DefaultSchedulePlanId INTEGER NULL,
                DefaultAutoCheckEnabled INTEGER NULL,
                DefaultCheckIntervalSeconds INTEGER NULL,
                DefaultPingTimeoutMs INTEGER NULL,
                DefaultFailureRetryIntervalSeconds INTEGER NULL,
                DefaultFailureRetryLimit INTEGER NULL,
                DefaultFailureThreshold INTEGER NULL,
                TargetAvailabilityPercent REAL NULL,
                CreatedAt TEXT NOT NULL,
                UpdatedAt TEXT NOT NULL
            );
            """);

        await EnsureColumnAsync(connection, "DeviceGroups", "Description", "TEXT NOT NULL DEFAULT ''");
        await EnsureColumnAsync(connection, "DeviceGroups", "DefaultSchedulePlanId", "INTEGER NULL");
        await EnsureColumnAsync(connection, "DeviceGroups", "DefaultAutoCheckEnabled", "INTEGER NULL");
        await EnsureColumnAsync(connection, "DeviceGroups", "DefaultCheckIntervalSeconds", "INTEGER NULL");
        await EnsureColumnAsync(connection, "DeviceGroups", "DefaultPingTimeoutMs", "INTEGER NULL");
        await EnsureColumnAsync(connection, "DeviceGroups", "DefaultFailureRetryIntervalSeconds", "INTEGER NULL");
        await EnsureColumnAsync(connection, "DeviceGroups", "DefaultFailureRetryLimit", "INTEGER NULL");
        await EnsureColumnAsync(connection, "DeviceGroups", "DefaultFailureThreshold", "INTEGER NULL");
        await EnsureColumnAsync(connection, "DeviceGroups", "TargetAvailabilityPercent", "REAL NULL");
        await EnsureColumnAsync(connection, "DeviceGroups", "CreatedAt", $"TEXT NOT NULL DEFAULT '{DateTime.Now:O}'");
        await EnsureColumnAsync(connection, "DeviceGroups", "UpdatedAt", $"TEXT NOT NULL DEFAULT '{DateTime.Now:O}'");
    }

    private static async Task CreateDevicesAsync(SqliteConnection connection)
    {
        await ExecuteAsync(connection, """
            CREATE TABLE IF NOT EXISTS Devices (
                Id INTEGER PRIMARY KEY AUTOINCREMENT,
                Name TEXT NOT NULL,
                IpAddress TEXT NOT NULL UNIQUE,
                DeviceType TEXT NOT NULL,
                Location TEXT NOT NULL DEFAULT '',
                GroupId INTEGER NULL,
                GroupName TEXT NOT NULL DEFAULT '',
                IsCritical INTEGER NOT NULL DEFAULT 0,
                IsActive INTEGER NOT NULL DEFAULT 1,
                IsEnabled INTEGER NOT NULL DEFAULT 1,
                IsDeleted INTEGER NOT NULL DEFAULT 0,
                DeletedAtUtc TEXT NULL,
                AutoCheckEnabled INTEGER NOT NULL DEFAULT 1,
                DefaultSchedulePlanId INTEGER NULL,
                PingTimeoutMs INTEGER NULL,
                CheckIntervalSeconds INTEGER NOT NULL DEFAULT 0,
                FailureRetryIntervalSeconds INTEGER NOT NULL DEFAULT 0,
                FailureRetryLimit INTEGER NOT NULL DEFAULT 0,
                FailureThreshold INTEGER NOT NULL DEFAULT 0,
                Description TEXT NOT NULL DEFAULT '',
                LastStatus TEXT NOT NULL DEFAULT 'Unknown',
                LastLatencyMs INTEGER NULL,
                LastCheckedAt TEXT NULL,
                LastSuccessfulCheckAt TEXT NULL,
                LastFailedCheckAt TEXT NULL,
                ConsecutiveFailures INTEGER NOT NULL DEFAULT 0,
                ConsecutiveSuccesses INTEGER NOT NULL DEFAULT 0,
                LastStableStatus TEXT NOT NULL DEFAULT 'Unknown',
                TargetAvailabilityPercent REAL NULL,
                CreatedAt TEXT NOT NULL,
                UpdatedAt TEXT NOT NULL
            );
            """);

        await EnsureColumnAsync(connection, "Devices", "Location", "TEXT NOT NULL DEFAULT ''");
        await EnsureColumnAsync(connection, "Devices", "GroupId", "INTEGER NULL");
        await EnsureColumnAsync(connection, "Devices", "GroupName", "TEXT NOT NULL DEFAULT ''");
        await EnsureColumnAsync(connection, "Devices", "IsCritical", "INTEGER NOT NULL DEFAULT 0");
        await EnsureColumnAsync(connection, "Devices", "IsActive", "INTEGER NOT NULL DEFAULT 1");
        await EnsureColumnAsync(connection, "Devices", "IsEnabled", "INTEGER NOT NULL DEFAULT 1");
        await EnsureColumnAsync(connection, "Devices", "IsDeleted", "INTEGER NOT NULL DEFAULT 0");
        await EnsureColumnAsync(connection, "Devices", "DeletedAtUtc", "TEXT NULL");
        await EnsureColumnAsync(connection, "Devices", "AutoCheckEnabled", "INTEGER NOT NULL DEFAULT 1");
        await EnsureColumnAsync(connection, "Devices", "DefaultSchedulePlanId", "INTEGER NULL");
        await EnsureColumnAsync(connection, "Devices", "PingTimeoutMs", "INTEGER NULL");
        await EnsureColumnAsync(connection, "Devices", "CheckIntervalSeconds", "INTEGER NOT NULL DEFAULT 0");
        await EnsureColumnAsync(connection, "Devices", "FailureRetryIntervalSeconds", "INTEGER NOT NULL DEFAULT 0");
        await EnsureColumnAsync(connection, "Devices", "FailureRetryLimit", "INTEGER NOT NULL DEFAULT 0");
        await EnsureColumnAsync(connection, "Devices", "FailureThreshold", "INTEGER NOT NULL DEFAULT 0");
        await EnsureColumnAsync(connection, "Devices", "Description", "TEXT NOT NULL DEFAULT ''");
        await EnsureColumnAsync(connection, "Devices", "LastStatus", "TEXT NOT NULL DEFAULT 'Unknown'");
        await EnsureColumnAsync(connection, "Devices", "LastLatencyMs", "INTEGER NULL");
        await EnsureColumnAsync(connection, "Devices", "LastCheckedAt", "TEXT NULL");
        await EnsureColumnAsync(connection, "Devices", "LastSuccessfulCheckAt", "TEXT NULL");
        await EnsureColumnAsync(connection, "Devices", "LastFailedCheckAt", "TEXT NULL");
        await EnsureColumnAsync(connection, "Devices", "ConsecutiveFailures", "INTEGER NOT NULL DEFAULT 0");
        await EnsureColumnAsync(connection, "Devices", "ConsecutiveSuccesses", "INTEGER NOT NULL DEFAULT 0");
        await EnsureColumnAsync(connection, "Devices", "LastStableStatus", "TEXT NOT NULL DEFAULT 'Unknown'");
        await EnsureColumnAsync(connection, "Devices", "TargetAvailabilityPercent", "REAL NULL");
        await EnsureColumnAsync(connection, "Devices", "CreatedAt", $"TEXT NOT NULL DEFAULT '{DateTime.Now:O}'");
        await EnsureColumnAsync(connection, "Devices", "UpdatedAt", $"TEXT NOT NULL DEFAULT '{DateTime.Now:O}'");
    }

    private static async Task CreateDeviceGroupMembersAsync(SqliteConnection connection)
    {
        await ExecuteAsync(connection, """
            CREATE TABLE IF NOT EXISTS DeviceGroupMembers (
                DeviceId INTEGER NOT NULL,
                GroupId INTEGER NOT NULL,
                CreatedAt TEXT NOT NULL,
                PRIMARY KEY (DeviceId, GroupId),
                FOREIGN KEY (DeviceId) REFERENCES Devices(Id) ON DELETE CASCADE,
                FOREIGN KEY (GroupId) REFERENCES DeviceGroups(Id) ON DELETE CASCADE
            );
            """);
    }

    private static async Task CreatePingLogsAsync(SqliteConnection connection)
    {
        await ExecuteAsync(connection, """
            CREATE TABLE IF NOT EXISTS PingLogs (
                Id INTEGER PRIMARY KEY AUTOINCREMENT,
                DeviceId INTEGER NULL,
                DeviceName TEXT NOT NULL,
                IpAddress TEXT NOT NULL,
                DeviceType TEXT NOT NULL,
                GroupName TEXT NOT NULL DEFAULT '',
                Status TEXT NOT NULL,
                IsReachable INTEGER NOT NULL DEFAULT 0,
                LatencyMs INTEGER NULL,
                ResponseMessage TEXT NOT NULL DEFAULT '',
                ErrorCode TEXT NOT NULL DEFAULT '',
                ErrorMessage TEXT NOT NULL DEFAULT '',
                CheckedAt TEXT NOT NULL,
                Source TEXT NOT NULL DEFAULT 'Manual',
                TriggerType TEXT NOT NULL DEFAULT 'Manual',
                PlanId INTEGER NULL,
                SchedulePlanId INTEGER NULL,
                SchedulePlanName TEXT NOT NULL DEFAULT '',
                WorkerInstanceId TEXT NOT NULL DEFAULT '',
                FOREIGN KEY (DeviceId) REFERENCES Devices(Id) ON DELETE SET NULL,
                FOREIGN KEY (SchedulePlanId) REFERENCES SchedulePlans(Id) ON DELETE SET NULL
            );
            """);

        await EnsureColumnAsync(connection, "PingLogs", "DeviceId", "INTEGER NULL");
        await EnsureColumnAsync(connection, "PingLogs", "DeviceName", "TEXT NOT NULL DEFAULT ''");
        await EnsureColumnAsync(connection, "PingLogs", "IpAddress", "TEXT NOT NULL DEFAULT ''");
        await EnsureColumnAsync(connection, "PingLogs", "DeviceType", "TEXT NOT NULL DEFAULT 'Other'");
        await EnsureColumnAsync(connection, "PingLogs", "GroupName", "TEXT NOT NULL DEFAULT ''");
        await EnsureColumnAsync(connection, "PingLogs", "Status", "TEXT NOT NULL DEFAULT 'Unknown'");
        await EnsureColumnAsync(connection, "PingLogs", "IsReachable", "INTEGER NOT NULL DEFAULT 0");
        await EnsureColumnAsync(connection, "PingLogs", "LatencyMs", "INTEGER NULL");
        await EnsureColumnAsync(connection, "PingLogs", "ResponseMessage", "TEXT NOT NULL DEFAULT ''");
        await EnsureColumnAsync(connection, "PingLogs", "ErrorCode", "TEXT NOT NULL DEFAULT ''");
        await EnsureColumnAsync(connection, "PingLogs", "ErrorMessage", "TEXT NOT NULL DEFAULT ''");
        await EnsureColumnAsync(connection, "PingLogs", "CheckedAt", $"TEXT NOT NULL DEFAULT '{DateTime.Now:O}'");
        await EnsureColumnAsync(connection, "PingLogs", "Source", "TEXT NOT NULL DEFAULT 'Manual'");
        await EnsureColumnAsync(connection, "PingLogs", "TriggerType", "TEXT NOT NULL DEFAULT 'Manual'");
        await EnsureColumnAsync(connection, "PingLogs", "PlanId", "INTEGER NULL");
        await EnsureColumnAsync(connection, "PingLogs", "SchedulePlanId", "INTEGER NULL");
        await EnsureColumnAsync(connection, "PingLogs", "SchedulePlanName", "TEXT NOT NULL DEFAULT ''");
        await EnsureColumnAsync(connection, "PingLogs", "WorkerInstanceId", "TEXT NOT NULL DEFAULT ''");
    }

    private static async Task CreateOutagesAsync(SqliteConnection connection)
    {
        await ExecuteAsync(connection, """
            CREATE TABLE IF NOT EXISTS Outages (
                Id INTEGER PRIMARY KEY AUTOINCREMENT,
                DeviceId INTEGER NOT NULL,
                StartedAt TEXT NOT NULL,
                EndedAt TEXT NULL,
                FailureCount INTEGER NOT NULL DEFAULT 0,
                RecoveryPingLogId INTEGER NULL,
                IsResolved INTEGER NOT NULL DEFAULT 0,
                CreatedAt TEXT NOT NULL,
                FOREIGN KEY (DeviceId) REFERENCES Devices(Id) ON DELETE CASCADE,
                FOREIGN KEY (RecoveryPingLogId) REFERENCES PingLogs(Id) ON DELETE SET NULL
            );
            """);

        await EnsureColumnAsync(connection, "Outages", "FailureCount", "INTEGER NOT NULL DEFAULT 0");
        await EnsureColumnAsync(connection, "Outages", "RecoveryPingLogId", "INTEGER NULL");
        await EnsureColumnAsync(connection, "Outages", "IsResolved", "INTEGER NOT NULL DEFAULT 0");
        await EnsureColumnAsync(connection, "Outages", "CreatedAt", $"TEXT NOT NULL DEFAULT '{DateTime.Now:O}'");
    }

    private static async Task CreateDeviceIncidentsAsync(SqliteConnection connection)
    {
        await ExecuteAsync(connection, """
            CREATE TABLE IF NOT EXISTS DeviceIncidents (
                Id INTEGER PRIMARY KEY AUTOINCREMENT,
                DeviceId INTEGER NOT NULL,
                StartedAtUtc TEXT NOT NULL,
                EndedAtUtc TEXT NULL,
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

        await EnsureColumnAsync(connection, "DeviceIncidents", "FlapCount", "INTEGER NOT NULL DEFAULT 0");
        await EnsureColumnAsync(connection, "DeviceIncidents", "FirstFailureAtUtc", "TEXT NULL");
        await EnsureColumnAsync(connection, "DeviceIncidents", "ConfirmedDownAtUtc", "TEXT NULL");
        await EnsureColumnAsync(connection, "DeviceIncidents", "DetectionDelaySeconds", "INTEGER NOT NULL DEFAULT 0");
    }

    private static async Task CreateNotificationOutboxAsync(SqliteConnection connection)
    {
        await ExecuteAsync(connection, """
            CREATE TABLE IF NOT EXISTS NotificationOutbox (
                Id INTEGER PRIMARY KEY AUTOINCREMENT,
                EventType TEXT NOT NULL,
                DeviceId INTEGER NULL,
                IncidentId INTEGER NULL,
                PayloadJson TEXT NOT NULL,
                DeduplicationKey TEXT NOT NULL,
                Status TEXT NOT NULL,
                AttemptCount INTEGER NOT NULL DEFAULT 0,
                NextAttemptAtUtc TEXT NOT NULL,
                LockedAtUtc TEXT NULL,
                LockedBy TEXT NOT NULL DEFAULT '',
                LastError TEXT NOT NULL DEFAULT '',
                CreatedAtUtc TEXT NOT NULL,
                SentAtUtc TEXT NULL,
                CancelledAtUtc TEXT NULL,
                FOREIGN KEY (DeviceId) REFERENCES Devices(Id) ON DELETE SET NULL,
                FOREIGN KEY (IncidentId) REFERENCES DeviceIncidents(Id) ON DELETE SET NULL
            );
            """);

        await EnsureColumnAsync(connection, "NotificationOutbox", "LastAttemptAtUtc", "TEXT NULL");
    }

    private static async Task CreateCsvImportAuditsAsync(SqliteConnection connection)
    {
        await ExecuteAsync(connection, """
            CREATE TABLE IF NOT EXISTS CsvImportAudits (
                Id INTEGER PRIMARY KEY AUTOINCREMENT,
                ImportedAtUtc TEXT NOT NULL,
                FileName TEXT NOT NULL,
                ImportMode TEXT NOT NULL,
                ImportScope TEXT NOT NULL,
                AddedCount INTEGER NOT NULL DEFAULT 0,
                UpdatedCount INTEGER NOT NULL DEFAULT 0,
                DeletedCount INTEGER NOT NULL DEFAULT 0,
                RestoredCount INTEGER NOT NULL DEFAULT 0,
                UnchangedCount INTEGER NOT NULL DEFAULT 0,
                SkippedCount INTEGER NOT NULL DEFAULT 0,
                InvalidRowCount INTEGER NOT NULL DEFAULT 0,
                DuplicateRowCount INTEGER NOT NULL DEFAULT 0,
                InitiatedBy TEXT NOT NULL,
                Result TEXT NOT NULL,
                ErrorMessage TEXT NOT NULL DEFAULT ''
            );
            """);
    }

    private static async Task CreateWorkerHeartbeatAsync(SqliteConnection connection)
    {
        await ExecuteAsync(connection, """
            CREATE TABLE IF NOT EXISTS WorkerHeartbeat (
                WorkerInstanceId TEXT PRIMARY KEY,
                MachineName TEXT NOT NULL,
                ProcessId INTEGER NOT NULL,
                Version TEXT NOT NULL,
                StartedAtUtc TEXT NOT NULL,
                LastSeenAtUtc TEXT NOT NULL,
                LastSchedulerCycleAtUtc TEXT NULL,
                LastSuccessfulPingAtUtc TEXT NULL,
                LastNotificationDispatchAtUtc TEXT NULL,
                Status TEXT NOT NULL,
                LastError TEXT NOT NULL DEFAULT '',
                LastCriticalError TEXT NOT NULL DEFAULT '',
                LastDatabaseLockedError TEXT NOT NULL DEFAULT '',
                LastSchedulerException TEXT NOT NULL DEFAULT '',
                LastNtfyException TEXT NOT NULL DEFAULT '',
                AverageSchedulerCycleMs REAL NOT NULL DEFAULT 0
            );
            """);
        await EnsureColumnAsync(connection, "WorkerHeartbeat", "LastCriticalError", "TEXT NOT NULL DEFAULT ''");
        await EnsureColumnAsync(connection, "WorkerHeartbeat", "LastDatabaseLockedError", "TEXT NOT NULL DEFAULT ''");
        await EnsureColumnAsync(connection, "WorkerHeartbeat", "LastSchedulerException", "TEXT NOT NULL DEFAULT ''");
        await EnsureColumnAsync(connection, "WorkerHeartbeat", "LastNtfyException", "TEXT NOT NULL DEFAULT ''");
        await EnsureColumnAsync(connection, "WorkerHeartbeat", "AverageSchedulerCycleMs", "REAL NOT NULL DEFAULT 0");
    }

    private static async Task CreateAvailabilitySchemaAsync(SqliteConnection connection)
    {
        await ExecuteAsync(connection, """
            CREATE TABLE IF NOT EXISTS DeviceAvailabilityPeriods (
                Id INTEGER PRIMARY KEY AUTOINCREMENT,
                DeviceId INTEGER NOT NULL,
                Status TEXT NOT NULL,
                StartedAtUtc TEXT NOT NULL,
                EndedAtUtc TEXT NULL,
                DurationSeconds INTEGER NULL,
                IncidentId INTEGER NULL,
                ReasonCode TEXT NOT NULL DEFAULT '',
                ReasonText TEXT NOT NULL DEFAULT '',
                DetectionSource TEXT NOT NULL DEFAULT '',
                FirstFailureAtUtc TEXT NULL,
                ConfirmedAtUtc TEXT NULL,
                CreatedAtUtc TEXT NOT NULL,
                UpdatedAtUtc TEXT NOT NULL,
                FOREIGN KEY (DeviceId) REFERENCES Devices(Id) ON DELETE CASCADE,
                FOREIGN KEY (IncidentId) REFERENCES DeviceIncidents(Id) ON DELETE SET NULL
            );
            """);

        await ExecuteAsync(connection, """
            CREATE TABLE IF NOT EXISTS MonitoringCalendars (
                Id INTEGER PRIMARY KEY AUTOINCREMENT,
                Name TEXT NOT NULL UNIQUE,
                TimezoneId TEXT NOT NULL,
                IsDefault INTEGER NOT NULL DEFAULT 0,
                CreatedAtUtc TEXT NOT NULL,
                UpdatedAtUtc TEXT NOT NULL
            );
            """);

        await ExecuteAsync(connection, """
            CREATE TABLE IF NOT EXISTS MonitoringCalendarRules (
                Id INTEGER PRIMARY KEY AUTOINCREMENT,
                CalendarId INTEGER NOT NULL,
                DayOfWeek INTEGER NOT NULL,
                StartTime TEXT NOT NULL,
                EndTime TEXT NOT NULL,
                IsEnabled INTEGER NOT NULL DEFAULT 1,
                FOREIGN KEY (CalendarId) REFERENCES MonitoringCalendars(Id) ON DELETE CASCADE
            );
            """);

        await ExecuteAsync(connection, """
            CREATE TABLE IF NOT EXISTS DeviceMonitoringCalendarAssignments (
                Id INTEGER PRIMARY KEY AUTOINCREMENT,
                TargetType TEXT NOT NULL,
                TargetId INTEGER NULL,
                CalendarId INTEGER NOT NULL,
                CreatedAtUtc TEXT NOT NULL,
                FOREIGN KEY (CalendarId) REFERENCES MonitoringCalendars(Id) ON DELETE CASCADE
            );
            """);

        await ExecuteAsync(connection, """
            CREATE TABLE IF NOT EXISTS MaintenanceWindows (
                Id INTEGER PRIMARY KEY AUTOINCREMENT,
                Name TEXT NOT NULL,
                StartedAtUtc TEXT NOT NULL,
                EndedAtUtc TEXT NOT NULL,
                Reason TEXT NOT NULL DEFAULT '',
                SuppressNotifications INTEGER NOT NULL DEFAULT 1,
                ContinuePings INTEGER NOT NULL DEFAULT 1,
                Status TEXT NOT NULL,
                CreatedBy TEXT NOT NULL DEFAULT '',
                CreatedAtUtc TEXT NOT NULL,
                UpdatedAtUtc TEXT NOT NULL
            );
            """);
        await EnsureColumnAsync(connection, "MaintenanceWindows", "SuppressNotifications", "INTEGER NOT NULL DEFAULT 1");
        await EnsureColumnAsync(connection, "MaintenanceWindows", "ContinuePings", "INTEGER NOT NULL DEFAULT 1");

        await ExecuteAsync(connection, """
            CREATE TABLE IF NOT EXISTS MaintenanceWindowTargets (
                Id INTEGER PRIMARY KEY AUTOINCREMENT,
                MaintenanceWindowId INTEGER NOT NULL,
                TargetType TEXT NOT NULL,
                TargetId INTEGER NULL,
                FOREIGN KEY (MaintenanceWindowId) REFERENCES MaintenanceWindows(Id) ON DELETE CASCADE
            );
            """);

        await ExecuteAsync(connection, """
            CREATE TABLE IF NOT EXISTS DeviceAvailabilityDaily (
                Id INTEGER PRIMARY KEY AUTOINCREMENT,
                DeviceId INTEGER NOT NULL,
                Date TEXT NOT NULL,
                TimezoneId TEXT NOT NULL,
                ExpectedMonitoringSeconds INTEGER NOT NULL DEFAULT 0,
                UpSeconds INTEGER NOT NULL DEFAULT 0,
                DownSeconds INTEGER NOT NULL DEFAULT 0,
                UnknownSeconds INTEGER NOT NULL DEFAULT 0,
                MaintenanceSeconds INTEGER NOT NULL DEFAULT 0,
                PausedSeconds INTEGER NOT NULL DEFAULT 0,
                IncidentCount INTEGER NOT NULL DEFAULT 0,
                RecoveredIncidentCount INTEGER NOT NULL DEFAULT 0,
                LongestOutageSeconds INTEGER NOT NULL DEFAULT 0,
                TotalDetectionDelaySeconds INTEGER NOT NULL DEFAULT 0,
                AvailabilityPercent REAL NULL,
                StrictAvailabilityPercent REAL NULL,
                CoveragePercent REAL NULL,
                CalculatedAtUtc TEXT NOT NULL,
                CalculationVersion INTEGER NOT NULL,
                FOREIGN KEY (DeviceId) REFERENCES Devices(Id) ON DELETE CASCADE,
                UNIQUE (DeviceId, Date, TimezoneId)
            );
            """);

        await ExecuteAsync(connection, """
            CREATE TABLE IF NOT EXISTS AvailabilityRecalculationAudits (
                Id INTEGER PRIMARY KEY AUTOINCREMENT,
                RequestedAtUtc TEXT NOT NULL,
                OperationType TEXT NOT NULL,
                DeviceId INTEGER NULL,
                GroupId INTEGER NULL,
                StartDate TEXT NOT NULL,
                EndDate TEXT NOT NULL,
                RequestedBy TEXT NOT NULL,
                Result TEXT NOT NULL,
                Message TEXT NOT NULL DEFAULT ''
            );
            """);

        var now = DateTime.UtcNow.ToString("O", CultureInfo.InvariantCulture);
        await ExecuteAsync(connection, $"""
            INSERT OR IGNORE INTO MonitoringCalendars
                (Name, TimezoneId, IsDefault, CreatedAtUtc, UpdatedAtUtc)
            VALUES
                ('24x7', '{TimeZoneInfo.Local.Id.Replace("'", "''")}', 1, '{now}', '{now}');
            """);
    }

    private static async Task CreateAppSettingsAsync(SqliteConnection connection)
    {
        await ExecuteAsync(connection, """
            CREATE TABLE IF NOT EXISTS AppSettings (
                Key TEXT PRIMARY KEY,
                Value TEXT NOT NULL
            );
            """);
    }

    private static async Task CreateIndexesAsync(SqliteConnection connection)
    {
        await ExecuteAsync(connection, "CREATE UNIQUE INDEX IF NOT EXISTS UX_Devices_IpAddress ON Devices(IpAddress);");
        await ExecuteAsync(connection, "CREATE INDEX IF NOT EXISTS IX_Devices_DeviceType ON Devices(DeviceType);");
        await ExecuteAsync(connection, "CREATE INDEX IF NOT EXISTS IX_Devices_Location ON Devices(Location);");
        await ExecuteAsync(connection, "CREATE INDEX IF NOT EXISTS IX_Devices_GroupId ON Devices(GroupId);");
        await ExecuteAsync(connection, "CREATE INDEX IF NOT EXISTS IX_Devices_GroupName ON Devices(GroupName);");
        await ExecuteAsync(connection, "CREATE INDEX IF NOT EXISTS IX_Devices_IsCritical ON Devices(IsCritical);");
        await ExecuteAsync(connection, "CREATE INDEX IF NOT EXISTS IX_Devices_IsActive ON Devices(IsActive);");
        await ExecuteAsync(connection, "CREATE INDEX IF NOT EXISTS IX_Devices_IsDeleted ON Devices(IsDeleted);");
        await ExecuteAsync(connection, "CREATE INDEX IF NOT EXISTS IX_Devices_IsEnabled ON Devices(IsEnabled);");
        await ExecuteAsync(connection, "CREATE INDEX IF NOT EXISTS IX_Devices_AutoCheck ON Devices(IsDeleted, IsEnabled, IsActive, AutoCheckEnabled, LastCheckedAt);");
        await ExecuteAsync(connection, "CREATE INDEX IF NOT EXISTS IX_Devices_AutoCheck_Status ON Devices(IsDeleted, IsEnabled, IsActive, AutoCheckEnabled, LastStatus, LastCheckedAt);");
        await ExecuteAsync(connection, "CREATE INDEX IF NOT EXISTS IX_Devices_DefaultSchedulePlanId ON Devices(DefaultSchedulePlanId);");
        await ExecuteAsync(connection, "CREATE INDEX IF NOT EXISTS IX_PingLogs_DeviceId ON PingLogs(DeviceId);");
        await ExecuteAsync(connection, "CREATE INDEX IF NOT EXISTS IX_PingLogs_CheckedAt ON PingLogs(CheckedAt DESC);");
        await ExecuteAsync(connection, "CREATE INDEX IF NOT EXISTS IX_PingLogs_IsReachable ON PingLogs(IsReachable);");
        await ExecuteAsync(connection, "CREATE INDEX IF NOT EXISTS IX_PingLogs_Status ON PingLogs(Status);");
        await ExecuteAsync(connection, "CREATE INDEX IF NOT EXISTS IX_PingLogs_ErrorCode ON PingLogs(ErrorCode);");
        await ExecuteAsync(connection, "CREATE INDEX IF NOT EXISTS IX_PingLogs_Source ON PingLogs(Source);");
        await ExecuteAsync(connection, "CREATE INDEX IF NOT EXISTS IX_PingLogs_PlanId ON PingLogs(PlanId);");
        await ExecuteAsync(connection, "CREATE INDEX IF NOT EXISTS IX_PingLogs_DeviceId_CheckedAt ON PingLogs(DeviceId, CheckedAt DESC);");
        await ExecuteAsync(connection, "CREATE INDEX IF NOT EXISTS IX_PingLogs_DeviceId_CheckedAt_Status ON PingLogs(DeviceId, CheckedAt DESC, Status);");
        await ExecuteAsync(connection, "CREATE INDEX IF NOT EXISTS IX_PingLogs_DeviceId_Status_CheckedAt ON PingLogs(DeviceId, Status, CheckedAt DESC);");
        await ExecuteAsync(connection, "CREATE INDEX IF NOT EXISTS IX_PingLogs_DeviceType ON PingLogs(DeviceType);");
        await ExecuteAsync(connection, "CREATE INDEX IF NOT EXISTS IX_PingLogs_GroupName ON PingLogs(GroupName);");
        await ExecuteAsync(connection, "CREATE INDEX IF NOT EXISTS IX_PingLogs_IpAddress ON PingLogs(IpAddress);");
        await ExecuteAsync(connection, "CREATE INDEX IF NOT EXISTS IX_PingLogs_TriggerType ON PingLogs(TriggerType);");
        await ExecuteAsync(connection, "CREATE INDEX IF NOT EXISTS IX_PingLogs_SchedulePlanId ON PingLogs(SchedulePlanId);");
        await ExecuteAsync(connection, "CREATE INDEX IF NOT EXISTS IX_PingLogs_CheckedAt_Status ON PingLogs(CheckedAt DESC, Status);");
        await ExecuteAsync(connection, "CREATE INDEX IF NOT EXISTS IX_Outages_DeviceId ON Outages(DeviceId);");
        await ExecuteAsync(connection, "CREATE INDEX IF NOT EXISTS IX_Outages_IsResolved ON Outages(IsResolved);");
        await ExecuteAsync(connection, "CREATE INDEX IF NOT EXISTS IX_Outages_DeviceId_IsResolved ON Outages(DeviceId, IsResolved);");
        await ExecuteAsync(connection, "CREATE INDEX IF NOT EXISTS IX_Outages_DeviceId_IsResolved_StartedAt ON Outages(DeviceId, IsResolved, StartedAt DESC);");
        await ExecuteAsync(connection, "CREATE INDEX IF NOT EXISTS IX_SchedulePlans_IsActive ON SchedulePlans(IsActive);");
        await ExecuteAsync(connection, "CREATE INDEX IF NOT EXISTS IX_SchedulePlans_IsActive_Target ON SchedulePlans(IsActive, TargetType, TargetValue);");
        await ExecuteAsync(connection, "CREATE INDEX IF NOT EXISTS IX_SchedulePlans_IsActive_NextRunAt ON SchedulePlans(IsActive, NextRunAt);");
        await ExecuteAsync(connection, "CREATE INDEX IF NOT EXISTS IX_DeviceIncidents_DeviceId ON DeviceIncidents(DeviceId);");
        await ExecuteAsync(connection, "CREATE INDEX IF NOT EXISTS IX_DeviceIncidents_Status ON DeviceIncidents(Status);");
        await ExecuteAsync(connection, "CREATE INDEX IF NOT EXISTS IX_DeviceIncidents_StartedAtUtc ON DeviceIncidents(StartedAtUtc);");
        await ExecuteAsync(connection, "CREATE INDEX IF NOT EXISTS IX_DeviceIncidents_EndedAtUtc ON DeviceIncidents(EndedAtUtc);");
        await ExecuteAsync(connection, "CREATE INDEX IF NOT EXISTS IX_DeviceIncidents_DeviceId_Status ON DeviceIncidents(DeviceId, Status);");
        await ExecuteAsync(connection, "CREATE UNIQUE INDEX IF NOT EXISTS UX_DeviceIncidents_Open_Device ON DeviceIncidents(DeviceId) WHERE Status = 'Open';");
        await ExecuteAsync(connection, "CREATE UNIQUE INDEX IF NOT EXISTS UX_NotificationOutbox_DeduplicationKey ON NotificationOutbox(DeduplicationKey);");
        await ExecuteAsync(connection, "CREATE INDEX IF NOT EXISTS IX_NotificationOutbox_Status_NextAttempt ON NotificationOutbox(Status, NextAttemptAtUtc);");
        await ExecuteAsync(connection, "CREATE INDEX IF NOT EXISTS IX_NotificationOutbox_DeviceId ON NotificationOutbox(DeviceId);");
        await ExecuteAsync(connection, "CREATE INDEX IF NOT EXISTS IX_NotificationOutbox_IncidentId ON NotificationOutbox(IncidentId);");
        await ExecuteAsync(connection, "CREATE INDEX IF NOT EXISTS IX_CsvImportAudits_ImportedAtUtc ON CsvImportAudits(ImportedAtUtc DESC);");
        await ExecuteAsync(connection, "CREATE INDEX IF NOT EXISTS IX_DeviceAvailabilityPeriods_DeviceId ON DeviceAvailabilityPeriods(DeviceId);");
        await ExecuteAsync(connection, "CREATE INDEX IF NOT EXISTS IX_DeviceAvailabilityPeriods_Status ON DeviceAvailabilityPeriods(Status);");
        await ExecuteAsync(connection, "CREATE INDEX IF NOT EXISTS IX_DeviceAvailabilityPeriods_StartedAtUtc ON DeviceAvailabilityPeriods(StartedAtUtc);");
        await ExecuteAsync(connection, "CREATE INDEX IF NOT EXISTS IX_DeviceAvailabilityPeriods_EndedAtUtc ON DeviceAvailabilityPeriods(EndedAtUtc);");
        await ExecuteAsync(connection, "CREATE INDEX IF NOT EXISTS IX_DeviceAvailabilityPeriods_Device_Started ON DeviceAvailabilityPeriods(DeviceId, StartedAtUtc);");
        await ExecuteAsync(connection, "CREATE INDEX IF NOT EXISTS IX_DeviceAvailabilityPeriods_Device_Ended ON DeviceAvailabilityPeriods(DeviceId, EndedAtUtc);");
        await ExecuteAsync(connection, "CREATE UNIQUE INDEX IF NOT EXISTS UX_DeviceAvailabilityPeriods_Open_Device ON DeviceAvailabilityPeriods(DeviceId) WHERE EndedAtUtc IS NULL;");
        await ExecuteAsync(connection, "CREATE INDEX IF NOT EXISTS IX_DeviceAvailabilityDaily_Device_Date ON DeviceAvailabilityDaily(DeviceId, Date);");
        await ExecuteAsync(connection, "CREATE INDEX IF NOT EXISTS IX_DeviceAvailabilityDaily_Date ON DeviceAvailabilityDaily(Date);");
        await ExecuteAsync(connection, "CREATE INDEX IF NOT EXISTS IX_MaintenanceWindows_Time ON MaintenanceWindows(StartedAtUtc, EndedAtUtc, Status);");
        await ExecuteAsync(connection, "CREATE INDEX IF NOT EXISTS IX_MaintenanceWindowTargets_Target ON MaintenanceWindowTargets(TargetType, TargetId);");
        await ExecuteAsync(connection, "CREATE INDEX IF NOT EXISTS IX_MonitoringAssignments_Target ON DeviceMonitoringCalendarAssignments(TargetType, TargetId);");
        await ExecuteAsync(connection, "CREATE INDEX IF NOT EXISTS IX_AvailabilityRecalcAudit_RequestedAt ON AvailabilityRecalculationAudits(RequestedAtUtc DESC);");
        await ExecuteAsync(connection, "CREATE INDEX IF NOT EXISTS IX_WorkerHeartbeat_LastSeenAtUtc ON WorkerHeartbeat(LastSeenAtUtc DESC);");
    }

    private static async Task MigrateLegacyGroupNamesAsync(SqliteConnection connection)
    {
        await ExecuteAsync(connection, """
            INSERT OR IGNORE INTO DeviceGroups (Name, Description, CreatedAt, UpdatedAt)
            SELECT DISTINCT TRIM(GroupName), '', strftime('%Y-%m-%dT%H:%M:%f', 'now'), strftime('%Y-%m-%dT%H:%M:%f', 'now')
            FROM Devices
            WHERE TRIM(GroupName) <> '';
            """);

        await ExecuteAsync(connection, """
            UPDATE Devices
            SET GroupId = (
                SELECT Id
                FROM DeviceGroups
                WHERE DeviceGroups.Name = Devices.GroupName
                LIMIT 1
            )
            WHERE GroupId IS NULL
              AND TRIM(GroupName) <> '';
            """);
    }

    private static async Task EnsureColumnAsync(
        SqliteConnection connection,
        string tableName,
        string columnName,
        string columnDefinition)
    {
        await using var pragma = connection.CreateCommand();
        pragma.CommandText = $"PRAGMA table_info({tableName});";

        await using var reader = await pragma.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            if (string.Equals(reader.GetString(1), columnName, StringComparison.OrdinalIgnoreCase))
            {
                return;
            }
        }

        await ExecuteAsync(connection, $"ALTER TABLE {tableName} ADD COLUMN {columnName} {columnDefinition};");
    }

    private static async Task ExecuteAsync(SqliteConnection connection, string commandText)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = commandText;
        await command.ExecuteNonQueryAsync();
    }
}
