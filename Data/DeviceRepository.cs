using System.Globalization;
using Microsoft.Data.Sqlite;
using NetworkHealthMonitor.Models;

namespace NetworkHealthMonitor.Data;

public sealed class DeviceRepository
{
    private readonly SqliteConnectionFactory _connectionFactory;

    public DeviceRepository(SqliteConnectionFactory connectionFactory)
    {
        _connectionFactory = connectionFactory;
    }

    public async Task<IReadOnlyList<Device>> GetAllAsync()
    {
        var devices = new List<Device>();
        await using var connection = await _connectionFactory.CreateOpenConnectionAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT Id, Name, IpAddress, DeviceType, Location, GroupId, GroupName, IsCritical,
                   IsActive, AutoCheckEnabled, DefaultSchedulePlanId, Description, LastStatus,
                   LastLatencyMs, LastCheckedAt, ConsecutiveFailures, ConsecutiveSuccesses,
                   LastStableStatus, CreatedAt, UpdatedAt
            FROM Devices
            ORDER BY Name COLLATE NOCASE, IpAddress;
            """;

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
                 AutoCheckEnabled, DefaultSchedulePlanId, Description, LastStatus, LastLatencyMs,
                 LastCheckedAt, ConsecutiveFailures, ConsecutiveSuccesses, LastStableStatus,
                 CreatedAt, UpdatedAt)
            VALUES
                (@Name, @IpAddress, @DeviceType, @Location, @GroupId, @GroupName, @IsCritical, @IsActive,
                 @AutoCheckEnabled, @DefaultSchedulePlanId, @Description, @LastStatus, @LastLatencyMs,
                 @LastCheckedAt, @ConsecutiveFailures, @ConsecutiveSuccesses, @LastStableStatus,
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
                Description = @Description,
                LastStatus = @LastStatus,
                LastLatencyMs = @LastLatencyMs,
                LastCheckedAt = @LastCheckedAt,
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

    public async Task BulkUpdatePingResultsAsync(IEnumerable<PingDeviceResult> results, int failureThreshold = 3)
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
                    ConsecutiveFailures = CASE WHEN @IsSuccess = 1 THEN 0 ELSE ConsecutiveFailures + 1 END,
                    ConsecutiveSuccesses = CASE WHEN @IsSuccess = 1 THEN ConsecutiveSuccesses + 1 ELSE 0 END,
                    LastStableStatus = CASE
                        WHEN @IsSuccess = 1 THEN @ReachableStatus
                        WHEN ConsecutiveFailures + 1 >= @FailureThreshold THEN @UnreachableStatus
                        ELSE LastStableStatus
                    END,
                    UpdatedAt = @UpdatedAt
                WHERE Id = @Id;
                """;

            AddParameter(command, "@LastStatus", result.Status.ToStorageValue());
            AddParameter(command, "@LastLatencyMs", result.LatencyMs);
            AddParameter(command, "@LastCheckedAt", ToStorageDate(result.CheckedAt));
            AddParameter(command, "@IsSuccess", result.IsSuccess ? 1 : 0);
            AddParameter(command, "@FailureThreshold", Math.Clamp(failureThreshold, 1, 20));
            AddParameter(command, "@ReachableStatus", DeviceStatus.Reachable.ToStorageValue());
            AddParameter(command, "@UnreachableStatus", DeviceStatus.Unreachable.ToStorageValue());
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
                        IsCritical = @IsCritical,
                        Description = @Description,
                        UpdatedAt = @UpdatedAt
                    WHERE Id = @Id;
                    """;
                AddParameter(update, "@Name", row.Name);
                AddParameter(update, "@DeviceType", row.DeviceType.ToStorageValue());
                AddParameter(update, "@Location", row.Location);
                AddParameter(update, "@GroupId", groupId);
                AddParameter(update, "@GroupName", row.GroupName);
                AddParameter(update, "@IsCritical", row.IsCritical ? 1 : 0);
                AddParameter(update, "@Description", row.Note);
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
                     AutoCheckEnabled, DefaultSchedulePlanId, Description, LastStatus, LastLatencyMs,
                     LastCheckedAt, ConsecutiveFailures, ConsecutiveSuccesses, LastStableStatus,
                     CreatedAt, UpdatedAt)
                VALUES
                    (@Name, @IpAddress, @DeviceType, @Location, @GroupId, @GroupName, @IsCritical, 1,
                     1, NULL, @Description, @LastStatus, NULL, NULL, 0, 0, @LastStableStatus,
                     @CreatedAt, @UpdatedAt);
                """;
            AddParameter(insert, "@Name", row.Name);
            AddParameter(insert, "@IpAddress", row.IpAddress);
            AddParameter(insert, "@DeviceType", row.DeviceType.ToStorageValue());
            AddParameter(insert, "@Location", row.Location);
            AddParameter(insert, "@GroupId", groupId);
            AddParameter(insert, "@GroupName", row.GroupName);
            AddParameter(insert, "@IsCritical", row.IsCritical ? 1 : 0);
            AddParameter(insert, "@Description", row.Note);
            AddParameter(insert, "@LastStatus", DeviceStatus.NotChecked.ToStorageValue());
            AddParameter(insert, "@LastStableStatus", DeviceStatus.NotChecked.ToStorageValue());
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
            Description = reader.GetString(11),
            LastStatus = DeviceStatusExtensions.FromStorageValue(reader.GetString(12)),
            LastLatencyMs = reader.IsDBNull(13) ? null : reader.GetInt64(13),
            LastCheckedAt = reader.IsDBNull(14) ? null : FromStorageDate(reader.GetString(14)),
            ConsecutiveFailures = reader.GetInt32(15),
            ConsecutiveSuccesses = reader.GetInt32(16),
            LastStableStatus = DeviceStatusExtensions.FromStorageValue(reader.GetString(17)),
            CreatedAt = FromStorageDate(reader.GetString(18)),
            UpdatedAt = FromStorageDate(reader.GetString(19))
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
        AddParameter(command, "@Description", device.Description);
        AddParameter(command, "@LastStatus", device.LastStatus.ToStorageValue());
        AddParameter(command, "@LastLatencyMs", device.LastLatencyMs);
        AddParameter(command, "@LastCheckedAt", device.LastCheckedAt.HasValue ? ToStorageDate(device.LastCheckedAt.Value) : null);
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
