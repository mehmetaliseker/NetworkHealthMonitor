using System.Globalization;
using Microsoft.Data.Sqlite;
using NetworkHealthMonitor.Models;

namespace NetworkHealthMonitor.Data;

public sealed class DeviceRepository
{
    private const string DeviceSelectColumns = """
        Id, Name, IpAddress, DeviceType, Location, GroupId, GroupName, IsCritical,
        IsActive, AutoCheckEnabled, DefaultSchedulePlanId, PingTimeoutMs, CheckIntervalSeconds,
        FailureRetryIntervalSeconds, FailureRetryLimit, FailureThreshold, Description, LastStatus,
        LastLatencyMs, LastCheckedAt, LastSuccessfulCheckAt, LastFailedCheckAt,
        ConsecutiveFailures, ConsecutiveSuccesses,
        LastStableStatus, CreatedAt, UpdatedAt
        """;

    private readonly SqliteConnectionFactory _connectionFactory;

    public DeviceRepository(SqliteConnectionFactory connectionFactory)
    {
        _connectionFactory = connectionFactory;
    }

    public async Task<IReadOnlyList<Device>> GetAllAsync()
    {
        return await GetDevicesAsync($"""
            SELECT {DeviceSelectColumns}
            FROM Devices
            ORDER BY Name COLLATE NOCASE, IpAddress;
            """);
    }

    public async Task<IReadOnlyList<Device>> GetAutoCheckCandidatesAsync()
    {
        return await GetDevicesAsync($"""
            SELECT {DeviceSelectColumns}
            FROM Devices
            WHERE IsActive = 1
              AND AutoCheckEnabled = 1
            ORDER BY Name COLLATE NOCASE, IpAddress;
            """);
    }

    private async Task<IReadOnlyList<Device>> GetDevicesAsync(string commandText)
    {
        var devices = new List<Device>();
        await using var connection = await _connectionFactory.CreateOpenConnectionAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = commandText;

        await using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            devices.Add(ReadDevice(reader));
        }

        return devices;
    }

    public async Task<int> AddAsync(Device device)
    {
        await using var connection = await _connectionFactory.CreateOpenConnectionAsync();
        using var transaction = connection.BeginTransaction();
        device.GroupId = await EnsureGroupIdAsync(connection, transaction, device.GroupName, device.GroupId);

        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            INSERT INTO Devices
                (Name, IpAddress, DeviceType, Location, GroupId, GroupName, IsCritical, IsActive,
                 AutoCheckEnabled, DefaultSchedulePlanId, PingTimeoutMs, CheckIntervalSeconds, FailureRetryIntervalSeconds,
                 FailureRetryLimit, FailureThreshold, Description, LastStatus, LastLatencyMs,
                 LastCheckedAt, LastSuccessfulCheckAt, LastFailedCheckAt,
                 ConsecutiveFailures, ConsecutiveSuccesses, LastStableStatus,
                 CreatedAt, UpdatedAt)
            VALUES
                (@Name, @IpAddress, @DeviceType, @Location, @GroupId, @GroupName, @IsCritical, @IsActive,
                 @AutoCheckEnabled, @DefaultSchedulePlanId, @PingTimeoutMs, @CheckIntervalSeconds, @FailureRetryIntervalSeconds,
                 @FailureRetryLimit, @FailureThreshold, @Description, @LastStatus, @LastLatencyMs,
                 @LastCheckedAt, @LastSuccessfulCheckAt, @LastFailedCheckAt,
                 @ConsecutiveFailures, @ConsecutiveSuccesses, @LastStableStatus,
                 @CreatedAt, @UpdatedAt);
            SELECT last_insert_rowid();
            """;

        AddDeviceParameters(command, device);
        var result = await command.ExecuteScalarAsync();
        transaction.Commit();

        var id = Convert.ToInt32(result, CultureInfo.InvariantCulture);
        device.Id = id;
        return id;
    }

    public async Task UpdateAsync(Device device)
    {
        device.UpdatedAt = DateTime.Now;
        await using var connection = await _connectionFactory.CreateOpenConnectionAsync();
        using var transaction = connection.BeginTransaction();
        device.GroupId = await EnsureGroupIdAsync(connection, transaction, device.GroupName, device.GroupId);

        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            UPDATE Devices
            SET Name = @Name,
                IpAddress = @IpAddress,
                DeviceType = @DeviceType,
                Location = @Location,
                GroupId = @GroupId,
                GroupName = @GroupName,
                IsCritical = @IsCritical,
                IsActive = @IsActive,
                AutoCheckEnabled = @AutoCheckEnabled,
                DefaultSchedulePlanId = @DefaultSchedulePlanId,
                PingTimeoutMs = @PingTimeoutMs,
                CheckIntervalSeconds = @CheckIntervalSeconds,
                FailureRetryIntervalSeconds = @FailureRetryIntervalSeconds,
                FailureRetryLimit = @FailureRetryLimit,
                FailureThreshold = @FailureThreshold,
                Description = @Description,
                LastStatus = @LastStatus,
                LastLatencyMs = @LastLatencyMs,
                LastCheckedAt = @LastCheckedAt,
                LastSuccessfulCheckAt = @LastSuccessfulCheckAt,
                LastFailedCheckAt = @LastFailedCheckAt,
                ConsecutiveFailures = @ConsecutiveFailures,
                ConsecutiveSuccesses = @ConsecutiveSuccesses,
                LastStableStatus = @LastStableStatus,
                CreatedAt = @CreatedAt,
                UpdatedAt = @UpdatedAt
            WHERE Id = @Id;
            """;

        AddDeviceParameters(command, device);
        AddParameter(command, "@Id", device.Id);
        await command.ExecuteNonQueryAsync();
        transaction.Commit();
    }

    public async Task BulkUpdatePingResultsAsync(IEnumerable<PingDeviceResult> results)
    {
        var materialized = results.ToList();
        if (materialized.Count == 0)
        {
            return;
        }

        await using var connection = await _connectionFactory.CreateOpenConnectionAsync();
        using var transaction = connection.BeginTransaction();

        foreach (var result in materialized)
        {
            await using var command = connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText = """
                UPDATE Devices
                SET LastStatus = @LastStatus,
                    LastLatencyMs = @LastLatencyMs,
                    LastCheckedAt = @LastCheckedAt,
                    LastSuccessfulCheckAt = CASE WHEN @IsSuccess = 1 THEN @LastCheckedAt ELSE LastSuccessfulCheckAt END,
                    LastFailedCheckAt = CASE WHEN @IsSuccess = 1 THEN LastFailedCheckAt ELSE @LastCheckedAt END,
                    ConsecutiveFailures = CASE WHEN @IsSuccess = 1 THEN 0 ELSE ConsecutiveFailures + 1 END,
                    ConsecutiveSuccesses = CASE WHEN @IsSuccess = 1 THEN ConsecutiveSuccesses + 1 ELSE 0 END,
                    LastStableStatus = CASE
                        WHEN @IsSuccess = 1 THEN @OnlineStatus
                        WHEN @LastStatus = @OfflineStatus THEN @OfflineStatus
                        ELSE LastStableStatus
                    END,
                    UpdatedAt = @UpdatedAt
                WHERE Id = @Id;
                """;

            AddParameter(command, "@LastStatus", result.Status.ToStorageValue());
            AddParameter(command, "@LastLatencyMs", result.LatencyMs);
            AddParameter(command, "@LastCheckedAt", ToStorageDate(result.CheckedAt));
            AddParameter(command, "@IsSuccess", result.IsSuccess ? 1 : 0);
            AddParameter(command, "@OnlineStatus", DeviceStatus.Online.ToStorageValue());
            AddParameter(command, "@OfflineStatus", DeviceStatus.Offline.ToStorageValue());
            AddParameter(command, "@UpdatedAt", ToStorageDate(DateTime.Now));
            AddParameter(command, "@Id", result.Device.Id);
            await command.ExecuteNonQueryAsync();
        }

        transaction.Commit();
    }

    public async Task<CsvImportApplyResult> ImportDevicesAsync(
        IEnumerable<DeviceCsvRecord> records,
        CsvImportDuplicateAction duplicateAction,
        int invalidRowCount)
    {
        if (duplicateAction == CsvImportDuplicateAction.Cancel)
        {
            return new CsvImportApplyResult(0, 0, 0, invalidRowCount);
        }

        var rows = records.ToList();
        if (rows.Count == 0)
        {
            return new CsvImportApplyResult(0, 0, 0, invalidRowCount);
        }

        var added = 0;
        var updated = 0;
        var skipped = 0;
        var now = DateTime.Now;

        await using var connection = await _connectionFactory.CreateOpenConnectionAsync();
        using var transaction = connection.BeginTransaction();

        foreach (var row in rows)
        {
            var groupId = await EnsureGroupIdAsync(connection, transaction, row.GroupName, null);
            var existingId = await GetDeviceIdByIpAsync(connection, transaction, row.IpAddress);
            if (existingId.HasValue)
            {
                if (duplicateAction == CsvImportDuplicateAction.SkipExisting)
                {
                    skipped++;
                    continue;
                }

                await using var update = connection.CreateCommand();
                update.Transaction = transaction;
                update.CommandText = """
                    UPDATE Devices
                    SET Name = @Name,
                        DeviceType = @DeviceType,
                        Location = @Location,
                        GroupId = @GroupId,
                        GroupName = @GroupName,
                        AutoCheckEnabled = @AutoCheckEnabled,
                        CheckIntervalSeconds = @CheckIntervalSeconds,
                        FailureRetryIntervalSeconds = @FailureRetryIntervalSeconds,
                        FailureRetryLimit = @FailureRetryLimit,
                        Description = @Description,
                        UpdatedAt = @UpdatedAt
                    WHERE Id = @Id;
                    """;
                AddParameter(update, "@Name", row.Name);
                AddParameter(update, "@DeviceType", row.DeviceType.ToStorageValue());
                AddParameter(update, "@Location", row.Location);
                AddParameter(update, "@GroupId", groupId);
                AddParameter(update, "@GroupName", row.GroupName);
                AddParameter(update, "@AutoCheckEnabled", row.AutoCheckEnabled ? 1 : 0);
                AddParameter(update, "@CheckIntervalSeconds", row.CheckIntervalSeconds);
                AddParameter(update, "@FailureRetryIntervalSeconds", row.RetryIntervalSeconds);
                AddParameter(update, "@FailureRetryLimit", row.RetryLimit);
                AddParameter(update, "@Description", row.Description);
                AddParameter(update, "@UpdatedAt", ToStorageDate(now));
                AddParameter(update, "@Id", existingId.Value);
                await update.ExecuteNonQueryAsync();
                updated++;
                continue;
            }

            await using var insert = connection.CreateCommand();
            insert.Transaction = transaction;
            insert.CommandText = """
                INSERT INTO Devices
                    (Name, IpAddress, DeviceType, Location, GroupId, GroupName, IsCritical, IsActive,
                     AutoCheckEnabled, DefaultSchedulePlanId, PingTimeoutMs, CheckIntervalSeconds, FailureRetryIntervalSeconds,
                     FailureRetryLimit, FailureThreshold, Description, LastStatus, LastLatencyMs,
                     LastCheckedAt, LastSuccessfulCheckAt, LastFailedCheckAt,
                     ConsecutiveFailures, ConsecutiveSuccesses, LastStableStatus,
                     CreatedAt, UpdatedAt)
                VALUES
                    (@Name, @IpAddress, @DeviceType, @Location, @GroupId, @GroupName, @IsCritical, 1,
                     @AutoCheckEnabled, NULL, NULL, @CheckIntervalSeconds, @FailureRetryIntervalSeconds,
                     @FailureRetryLimit, 0, @Description, @LastStatus, NULL, NULL, NULL, NULL, 0, 0, @LastStableStatus,
                     @CreatedAt, @UpdatedAt);
                """;
            AddParameter(insert, "@Name", row.Name);
            AddParameter(insert, "@IpAddress", row.IpAddress);
            AddParameter(insert, "@DeviceType", row.DeviceType.ToStorageValue());
            AddParameter(insert, "@Location", row.Location);
            AddParameter(insert, "@GroupId", groupId);
            AddParameter(insert, "@GroupName", row.GroupName);
            AddParameter(insert, "@IsCritical", 0);
            AddParameter(insert, "@AutoCheckEnabled", row.AutoCheckEnabled ? 1 : 0);
            AddParameter(insert, "@CheckIntervalSeconds", row.CheckIntervalSeconds);
            AddParameter(insert, "@FailureRetryIntervalSeconds", row.RetryIntervalSeconds);
            AddParameter(insert, "@FailureRetryLimit", row.RetryLimit);
            AddParameter(insert, "@Description", row.Description);
            AddParameter(insert, "@LastStatus", DeviceStatus.Unknown.ToStorageValue());
            AddParameter(insert, "@LastStableStatus", DeviceStatus.Unknown.ToStorageValue());
            AddParameter(insert, "@CreatedAt", ToStorageDate(now));
            AddParameter(insert, "@UpdatedAt", ToStorageDate(now));
            await insert.ExecuteNonQueryAsync();
            added++;
        }

        transaction.Commit();
        return new CsvImportApplyResult(added, updated, skipped, invalidRowCount);
    }

    public async Task DeleteAsync(int id)
    {
        await using var connection = await _connectionFactory.CreateOpenConnectionAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = "DELETE FROM Devices WHERE Id = @Id;";
        AddParameter(command, "@Id", id);
        await command.ExecuteNonQueryAsync();
    }

    public async Task<int> BulkSetAutoCheckAsync(IEnumerable<int> deviceIds, bool enabled)
    {
        return await BulkUpdateAsync(
            deviceIds,
            "UPDATE Devices SET AutoCheckEnabled = @Value, UpdatedAt = @UpdatedAt WHERE Id = @Id;",
            command => AddParameter(command, "@Value", enabled ? 1 : 0));
    }

    public async Task<int> BulkSetActiveAsync(IEnumerable<int> deviceIds, bool isActive)
    {
        return await BulkUpdateAsync(
            deviceIds,
            "UPDATE Devices SET IsActive = @Value, UpdatedAt = @UpdatedAt WHERE Id = @Id;",
            command => AddParameter(command, "@Value", isActive ? 1 : 0));
    }

    public async Task<int> BulkSetGroupAsync(IEnumerable<int> deviceIds, int? groupId, string groupName)
    {
        return await BulkUpdateAsync(
            deviceIds,
            """
            UPDATE Devices
            SET GroupId = @GroupId,
                GroupName = @GroupName,
                UpdatedAt = @UpdatedAt
            WHERE Id = @Id;
            """,
            command =>
            {
                AddParameter(command, "@GroupId", groupId);
                AddParameter(command, "@GroupName", groupName.Trim());
            });
    }

    public async Task<int> BulkSetCheckIntervalAsync(IEnumerable<int> deviceIds, int checkIntervalSeconds)
    {
        var normalized = checkIntervalSeconds <= 0
            ? 0
            : Math.Clamp(checkIntervalSeconds, AppSettings.MinDeviceCheckIntervalSeconds, AppSettings.MaxDeviceCheckIntervalSeconds);
        return await BulkUpdateAsync(
            deviceIds,
            "UPDATE Devices SET CheckIntervalSeconds = @Value, UpdatedAt = @UpdatedAt WHERE Id = @Id;",
            command => AddParameter(command, "@Value", normalized));
    }

    public async Task<bool> ExistsByIpAsync(string ipAddress, int? excludeDeviceId = null)
    {
        await using var connection = await _connectionFactory.CreateOpenConnectionAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT COUNT(1)
            FROM Devices
            WHERE IpAddress = @IpAddress
              AND (@ExcludeId IS NULL OR Id <> @ExcludeId);
            """;

        AddParameter(command, "@IpAddress", ipAddress);
        AddParameter(command, "@ExcludeId", excludeDeviceId);
        var result = await command.ExecuteScalarAsync();
        return Convert.ToInt32(result, CultureInfo.InvariantCulture) > 0;
    }

    private async Task<int> BulkUpdateAsync(
        IEnumerable<int> deviceIds,
        string commandText,
        Action<SqliteCommand> addParameters)
    {
        var ids = deviceIds.Where(id => id > 0).Distinct().ToList();
        if (ids.Count == 0)
        {
            return 0;
        }

        var affected = 0;
        await using var connection = await _connectionFactory.CreateOpenConnectionAsync();
        using var transaction = connection.BeginTransaction();
        foreach (var id in ids)
        {
            await using var command = connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText = commandText;
            addParameters(command);
            AddParameter(command, "@UpdatedAt", ToStorageDate(DateTime.Now));
            AddParameter(command, "@Id", id);
            affected += await command.ExecuteNonQueryAsync();
        }

        transaction.Commit();
        return affected;
    }

    public async Task<int> CountByGroupAsync(int groupId)
    {
        await using var connection = await _connectionFactory.CreateOpenConnectionAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT COUNT(1) FROM Devices WHERE GroupId = @GroupId;";
        AddParameter(command, "@GroupId", groupId);
        var result = await command.ExecuteScalarAsync();
        return Convert.ToInt32(result, CultureInfo.InvariantCulture);
    }

    private static async Task<int?> EnsureGroupIdAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        string groupName,
        int? currentGroupId)
    {
        if (currentGroupId.HasValue)
        {
            return currentGroupId;
        }

        if (string.IsNullOrWhiteSpace(groupName))
        {
            return null;
        }

        await using var insert = connection.CreateCommand();
        insert.Transaction = transaction;
        insert.CommandText = """
            INSERT OR IGNORE INTO DeviceGroups (Name, Description, CreatedAt, UpdatedAt)
            VALUES (@Name, '', @CreatedAt, @UpdatedAt);
            """;
        AddParameter(insert, "@Name", groupName.Trim());
        AddParameter(insert, "@CreatedAt", ToStorageDate(DateTime.Now));
        AddParameter(insert, "@UpdatedAt", ToStorageDate(DateTime.Now));
        await insert.ExecuteNonQueryAsync();

        await using var select = connection.CreateCommand();
        select.Transaction = transaction;
        select.CommandText = "SELECT Id FROM DeviceGroups WHERE Name = @Name LIMIT 1;";
        AddParameter(select, "@Name", groupName.Trim());
        var result = await select.ExecuteScalarAsync();
        return result is null || result == DBNull.Value
            ? null
            : Convert.ToInt32(result, CultureInfo.InvariantCulture);
    }

    private static async Task<int?> GetDeviceIdByIpAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        string ipAddress)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = "SELECT Id FROM Devices WHERE IpAddress = @IpAddress LIMIT 1;";
        AddParameter(command, "@IpAddress", ipAddress);
        var result = await command.ExecuteScalarAsync();
        return result is null || result == DBNull.Value
            ? null
            : Convert.ToInt32(result, CultureInfo.InvariantCulture);
    }

    private static Device ReadDevice(SqliteDataReader reader)
    {
        return new Device
        {
            Id = reader.GetInt32(0),
            Name = reader.GetString(1),
            IpAddress = reader.GetString(2),
            DeviceType = DeviceTypeExtensions.FromStorageValue(reader.GetString(3)),
            Location = reader.GetString(4),
            GroupId = reader.IsDBNull(5) ? null : reader.GetInt32(5),
            GroupName = reader.GetString(6),
            IsCritical = reader.GetInt32(7) == 1,
            IsActive = reader.GetInt32(8) == 1,
            AutoCheckEnabled = reader.GetInt32(9) == 1,
            DefaultSchedulePlanId = reader.IsDBNull(10) ? null : reader.GetInt32(10),
            PingTimeoutMs = reader.IsDBNull(11) ? null : reader.GetInt32(11),
            CheckIntervalSeconds = reader.GetInt32(12),
            FailureRetryIntervalSeconds = reader.GetInt32(13),
            FailureRetryLimit = reader.GetInt32(14),
            FailureThreshold = reader.GetInt32(15),
            Description = reader.GetString(16),
            LastStatus = DeviceStatusExtensions.FromStorageValue(reader.GetString(17)),
            LastLatencyMs = reader.IsDBNull(18) ? null : reader.GetInt64(18),
            LastCheckedAt = reader.IsDBNull(19) ? null : FromStorageDate(reader.GetString(19)),
            LastSuccessfulCheckAt = reader.IsDBNull(20) ? null : FromStorageDate(reader.GetString(20)),
            LastFailedCheckAt = reader.IsDBNull(21) ? null : FromStorageDate(reader.GetString(21)),
            ConsecutiveFailures = reader.GetInt32(22),
            ConsecutiveSuccesses = reader.GetInt32(23),
            LastStableStatus = DeviceStatusExtensions.FromStorageValue(reader.GetString(24)),
            CreatedAt = FromStorageDate(reader.GetString(25)),
            UpdatedAt = FromStorageDate(reader.GetString(26))
        };
    }

    private static void AddDeviceParameters(SqliteCommand command, Device device)
    {
        AddParameter(command, "@Name", device.Name.Trim());
        AddParameter(command, "@IpAddress", device.IpAddress.Trim());
        AddParameter(command, "@DeviceType", device.DeviceType.ToStorageValue());
        AddParameter(command, "@Location", device.Location.Trim());
        AddParameter(command, "@GroupId", device.GroupId);
        AddParameter(command, "@GroupName", device.GroupName.Trim());
        AddParameter(command, "@IsCritical", device.IsCritical ? 1 : 0);
        AddParameter(command, "@IsActive", device.IsActive ? 1 : 0);
        AddParameter(command, "@AutoCheckEnabled", device.AutoCheckEnabled ? 1 : 0);
        AddParameter(command, "@DefaultSchedulePlanId", device.DefaultSchedulePlanId);
        AddParameter(command, "@PingTimeoutMs", device.PingTimeoutMs);
        AddParameter(command, "@CheckIntervalSeconds", device.CheckIntervalSeconds <= 0 ? 0 : Math.Clamp(device.CheckIntervalSeconds, AppSettings.MinDeviceCheckIntervalSeconds, AppSettings.MaxDeviceCheckIntervalSeconds));
        AddParameter(command, "@FailureRetryIntervalSeconds", device.FailureRetryIntervalSeconds <= 0 ? 0 : Math.Clamp(device.FailureRetryIntervalSeconds, AppSettings.MinFailureRetryIntervalSeconds, AppSettings.MaxFailureRetryIntervalSeconds));
        AddParameter(command, "@FailureRetryLimit", device.FailureRetryLimit <= 0 ? 0 : Math.Clamp(device.FailureRetryLimit, AppSettings.MinFailureRetryLimit, AppSettings.MaxFailureRetryLimit));
        AddParameter(command, "@FailureThreshold", device.FailureThreshold <= 0 ? 0 : Math.Clamp(device.FailureThreshold, AppSettings.MinFailureThreshold, AppSettings.MaxFailureThreshold));
        AddParameter(command, "@Description", device.Description);
        AddParameter(command, "@LastStatus", device.LastStatus.ToStorageValue());
        AddParameter(command, "@LastLatencyMs", device.LastLatencyMs);
        AddParameter(command, "@LastCheckedAt", device.LastCheckedAt.HasValue ? ToStorageDate(device.LastCheckedAt.Value) : null);
        AddParameter(command, "@LastSuccessfulCheckAt", device.LastSuccessfulCheckAt.HasValue ? ToStorageDate(device.LastSuccessfulCheckAt.Value) : null);
        AddParameter(command, "@LastFailedCheckAt", device.LastFailedCheckAt.HasValue ? ToStorageDate(device.LastFailedCheckAt.Value) : null);
        AddParameter(command, "@ConsecutiveFailures", device.ConsecutiveFailures);
        AddParameter(command, "@ConsecutiveSuccesses", device.ConsecutiveSuccesses);
        AddParameter(command, "@LastStableStatus", device.LastStableStatus.ToStorageValue());
        AddParameter(command, "@CreatedAt", ToStorageDate(device.CreatedAt));
        AddParameter(command, "@UpdatedAt", ToStorageDate(device.UpdatedAt));
    }

    private static void AddParameter(SqliteCommand command, string name, object? value)
    {
        command.Parameters.AddWithValue(name, value ?? DBNull.Value);
    }

    private static string ToStorageDate(DateTime value)
    {
        return value.ToString("O", CultureInfo.InvariantCulture);
    }

    private static DateTime FromStorageDate(string value)
    {
        return DateTime.TryParse(
            value,
            CultureInfo.InvariantCulture,
            DateTimeStyles.RoundtripKind,
            out var parsed)
            ? parsed
            : DateTime.Now;
    }
}
