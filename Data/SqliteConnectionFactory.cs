using System.IO;
using Microsoft.Data.Sqlite;
using NetworkHealthMonitor.Infrastructure;
using NetworkHealthMonitor.Models;

namespace NetworkHealthMonitor.Data;

public sealed class SqliteConnectionFactory
{
    private readonly string _connectionString;

    public SqliteConnectionFactory()
    {
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

    public async Task InitializeAsync()
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

        await CreateSchedulePlansAsync(connection);
        await CreateDeviceGroupsAsync(connection);
        await CreateDevicesAsync(connection);
        await CreateDeviceGroupMembersAsync(connection);
        await CreatePingLogsAsync(connection);
        await CreateOutagesAsync(connection);
        await CreateAppSettingsAsync(connection);
        await CreateIndexesAsync(connection);
        await MigrateLegacyGroupNamesAsync(connection);
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
                CreatedAt TEXT NOT NULL,
                UpdatedAt TEXT NOT NULL
            );
            """);

        await EnsureColumnAsync(connection, "Devices", "Location", "TEXT NOT NULL DEFAULT ''");
        await EnsureColumnAsync(connection, "Devices", "GroupId", "INTEGER NULL");
        await EnsureColumnAsync(connection, "Devices", "GroupName", "TEXT NOT NULL DEFAULT ''");
        await EnsureColumnAsync(connection, "Devices", "IsCritical", "INTEGER NOT NULL DEFAULT 0");
        await EnsureColumnAsync(connection, "Devices", "IsActive", "INTEGER NOT NULL DEFAULT 1");
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
                LatencyMs INTEGER NULL,
                ResponseMessage TEXT NOT NULL DEFAULT '',
                ErrorMessage TEXT NOT NULL DEFAULT '',
                CheckedAt TEXT NOT NULL,
                TriggerType TEXT NOT NULL DEFAULT 'Manual',
                SchedulePlanId INTEGER NULL,
                SchedulePlanName TEXT NOT NULL DEFAULT '',
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
        await EnsureColumnAsync(connection, "PingLogs", "LatencyMs", "INTEGER NULL");
        await EnsureColumnAsync(connection, "PingLogs", "ResponseMessage", "TEXT NOT NULL DEFAULT ''");
        await EnsureColumnAsync(connection, "PingLogs", "ErrorMessage", "TEXT NOT NULL DEFAULT ''");
        await EnsureColumnAsync(connection, "PingLogs", "CheckedAt", $"TEXT NOT NULL DEFAULT '{DateTime.Now:O}'");
        await EnsureColumnAsync(connection, "PingLogs", "TriggerType", "TEXT NOT NULL DEFAULT 'Manual'");
        await EnsureColumnAsync(connection, "PingLogs", "SchedulePlanId", "INTEGER NULL");
        await EnsureColumnAsync(connection, "PingLogs", "SchedulePlanName", "TEXT NOT NULL DEFAULT ''");
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
        await ExecuteAsync(connection, "CREATE INDEX IF NOT EXISTS IX_Devices_AutoCheck ON Devices(IsActive, AutoCheckEnabled, LastCheckedAt);");
        await ExecuteAsync(connection, "CREATE INDEX IF NOT EXISTS IX_Devices_AutoCheck_Status ON Devices(IsActive, AutoCheckEnabled, LastStatus, LastCheckedAt);");
        await ExecuteAsync(connection, "CREATE INDEX IF NOT EXISTS IX_Devices_DefaultSchedulePlanId ON Devices(DefaultSchedulePlanId);");
        await ExecuteAsync(connection, "CREATE INDEX IF NOT EXISTS IX_PingLogs_DeviceId ON PingLogs(DeviceId);");
        await ExecuteAsync(connection, "CREATE INDEX IF NOT EXISTS IX_PingLogs_CheckedAt ON PingLogs(CheckedAt DESC);");
        await ExecuteAsync(connection, "CREATE INDEX IF NOT EXISTS IX_PingLogs_Status ON PingLogs(Status);");
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
