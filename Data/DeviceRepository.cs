using System.Globalization;
using Microsoft.Data.Sqlite;
using NetworkHealthMonitor.Models;

namespace NetworkHealthMonitor.Data;

public sealed class DeviceRepository
{
    private const string DeviceSelectColumns = """
        Id, Name, IpAddress, DeviceType, Location, GroupId, GroupName, IsCritical,
        IsActive, IsEnabled, IsDeleted, DeletedAtUtc, AutoCheckEnabled, DefaultSchedulePlanId, PingTimeoutMs, CheckIntervalSeconds,
        FailureRetryIntervalSeconds, FailureRetryLimit, FailureThreshold, Description, LastStatus,
        LastLatencyMs, LastCheckedAt, LastSuccessfulCheckAt, LastFailedCheckAt,
        ConsecutiveFailures, ConsecutiveSuccesses,
        LastStableStatus, SuppressionMode, SuppressedFromUtc, SuppressedUntilUtc,
        SuppressionReason, SuppressedBy, CreatedAt, UpdatedAt, TargetAvailabilityPercent
        """;

    private readonly SqliteConnectionFactory _connectionFactory;

    public DeviceRepository(SqliteConnectionFactory connectionFactory)
    {
        _connectionFactory = connectionFactory;
    }

    public SqliteConnectionFactory ConnectionFactory => _connectionFactory;

    public async Task<IReadOnlyList<Device>> GetAllAsync(bool includeDeleted = false, bool onlyDeleted = false)
    {
        var deletedFilter = onlyDeleted
            ? "WHERE IsDeleted = 1"
            : includeDeleted
                ? string.Empty
                : "WHERE IsDeleted = 0";

        return await GetDevicesAsync($"""
            SELECT {DeviceSelectColumns}
            FROM Devices
            {deletedFilter}
            ORDER BY Name COLLATE NOCASE, IpAddress;
            """);
    }

    public async Task<Device?> GetByIdAsync(int id, bool includeDeleted = false)
    {
        var devices = await GetDevicesAsync($"""
            SELECT {DeviceSelectColumns}
            FROM Devices
            WHERE Id = {id}
              {(includeDeleted ? string.Empty : "AND IsDeleted = 0")}
            LIMIT 1;
            """);
        return devices.FirstOrDefault();
    }

    public async Task<Device?> GetActiveByIdAsync(int id)
    {
        var devices = await GetDevicesAsync($"""
            SELECT {DeviceSelectColumns}
            FROM Devices
            WHERE Id = {id}
              AND IsDeleted = 0
              AND IsEnabled = 1
              AND IsActive = 1
            LIMIT 1;
            """);
        return devices.FirstOrDefault();
    }

    public async Task<IReadOnlyList<Device>> GetAutoCheckCandidatesAsync()
    {
        var nowUtc = ToStorageDate(DateTime.UtcNow);
        return await GetDevicesAsync($"""
            SELECT {DeviceSelectColumns}
            FROM Devices
            WHERE IsDeleted = 0
              AND IsEnabled = 1
              AND IsActive = 1
              AND AutoCheckEnabled = 1
              AND NOT (
                  SuppressionMode = 'PauseMonitoring'
                  AND (SuppressedUntilUtc IS NULL OR SuppressedUntilUtc > '{nowUtc}')
              )
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
                 IsEnabled, IsDeleted, DeletedAtUtc, AutoCheckEnabled, DefaultSchedulePlanId, PingTimeoutMs, CheckIntervalSeconds, FailureRetryIntervalSeconds,
                 FailureRetryLimit, FailureThreshold, Description, LastStatus, LastLatencyMs,
                 LastCheckedAt, LastSuccessfulCheckAt, LastFailedCheckAt,
                 ConsecutiveFailures, ConsecutiveSuccesses, LastStableStatus,
                 SuppressionMode, SuppressedFromUtc, SuppressedUntilUtc, SuppressionReason, SuppressedBy,
                 CreatedAt, UpdatedAt, TargetAvailabilityPercent)
            VALUES
                (@Name, @IpAddress, @DeviceType, @Location, @GroupId, @GroupName, @IsCritical, @IsActive,
                 @IsEnabled, @IsDeleted, @DeletedAtUtc, @AutoCheckEnabled, @DefaultSchedulePlanId, @PingTimeoutMs, @CheckIntervalSeconds, @FailureRetryIntervalSeconds,
                 @FailureRetryLimit, @FailureThreshold, @Description, @LastStatus, @LastLatencyMs,
                 @LastCheckedAt, @LastSuccessfulCheckAt, @LastFailedCheckAt,
                 @ConsecutiveFailures, @ConsecutiveSuccesses, @LastStableStatus,
                 @SuppressionMode, @SuppressedFromUtc, @SuppressedUntilUtc, @SuppressionReason, @SuppressedBy,
                 @CreatedAt, @UpdatedAt, @TargetAvailabilityPercent);
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
                IsEnabled = @IsEnabled,
                IsDeleted = @IsDeleted,
                DeletedAtUtc = @DeletedAtUtc,
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
                SuppressionMode = @SuppressionMode,
                SuppressedFromUtc = @SuppressedFromUtc,
                SuppressedUntilUtc = @SuppressedUntilUtc,
                SuppressionReason = @SuppressionReason,
                SuppressedBy = @SuppressedBy,
                CreatedAt = @CreatedAt,
                UpdatedAt = @UpdatedAt,
                TargetAvailabilityPercent = @TargetAvailabilityPercent
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
                WHERE Id = @Id
                  AND IsDeleted = 0
                  AND IsEnabled = 1;
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
            return new CsvImportApplyResult(0, 0, 0, 0, 0, 0, invalidRowCount, string.Empty, 0);
        }

        var rows = records.ToList();
        if (rows.Count == 0)
        {
            return new CsvImportApplyResult(0, 0, 0, 0, 0, 0, invalidRowCount, string.Empty, 0);
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
                        IsActive = 1,
                        IsEnabled = 1,
                        IsDeleted = 0,
                        DeletedAtUtc = NULL,
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
                     IsEnabled, IsDeleted, DeletedAtUtc, AutoCheckEnabled, DefaultSchedulePlanId, PingTimeoutMs, CheckIntervalSeconds, FailureRetryIntervalSeconds,
                     FailureRetryLimit, FailureThreshold, Description, LastStatus, LastLatencyMs,
                     LastCheckedAt, LastSuccessfulCheckAt, LastFailedCheckAt,
                     ConsecutiveFailures, ConsecutiveSuccesses, LastStableStatus,
                     CreatedAt, UpdatedAt)
                VALUES
                    (@Name, @IpAddress, @DeviceType, @Location, @GroupId, @GroupName, @IsCritical, 1,
                     1, 0, NULL, @AutoCheckEnabled, NULL, NULL, @CheckIntervalSeconds, @FailureRetryIntervalSeconds,
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
        return new CsvImportApplyResult(added, updated, 0, 0, 0, skipped, invalidRowCount, string.Empty, 0);
    }

    public async Task<CsvImportApplyResult> ApplyCsvImportAsync(
        CsvImportPreview preview,
        CsvImportOptions options,
        string backupPath)
    {
        if (preview.HasBlockingErrors)
        {
            var rejectedAuditId = await InsertCsvAuditAsync(options, preview, 0, 0, 0, 0, 0, preview.SkipCount, "Rejected", "Preview contains invalid, duplicate, or zero valid rows.");
            return new CsvImportApplyResult(0, 0, 0, 0, 0, preview.SkipCount, preview.InvalidRowCount, backupPath, rejectedAuditId);
        }

        var added = 0;
        var updated = 0;
        var deleted = 0;
        var restored = 0;
        var unchanged = 0;
        var skipped = 0;
        long auditId;
        var nowUtc = DateTime.UtcNow;

        await using var connection = await _connectionFactory.CreateOpenConnectionAsync();
        using var transaction = connection.BeginTransaction();
        try
        {
            foreach (var row in preview.Rows)
            {
                switch (row.Status)
                {
                    case CsvImportRowStatus.Add:
                        await InsertDeviceFromCsvAsync(connection, transaction, row.Record ?? throw new InvalidOperationException("CSV add row has no record."), nowUtc);
                        added++;
                        break;
                    case CsvImportRowStatus.Update:
                        await UpdateDeviceFromCsvByIpAsync(connection, transaction, row.Record ?? throw new InvalidOperationException("CSV update row has no record."), restore: false, nowUtc);
                        updated++;
                        break;
                    case CsvImportRowStatus.Restore:
                        await UpdateDeviceFromCsvByIpAsync(connection, transaction, row.Record ?? throw new InvalidOperationException("CSV restore row has no record."), restore: true, nowUtc);
                        restored++;
                        break;
                    case CsvImportRowStatus.Delete:
                        if (!row.ExistingDeviceId.HasValue)
                        {
                            throw new InvalidOperationException("CSV delete row has no device id.");
                        }

                        deleted += await SoftDeleteCoreAsync(connection, transaction, row.ExistingDeviceId.Value, nowUtc);
                        break;
                    case CsvImportRowStatus.Unchanged:
                        unchanged++;
                        break;
                    case CsvImportRowStatus.Skip:
                        skipped++;
                        break;
                }
            }

            auditId = await InsertCsvAuditAsync(connection, transaction, options, preview, added, updated, deleted, restored, unchanged, skipped, "Succeeded", string.Empty);
            transaction.Commit();
        }
        catch (Exception ex)
        {
            transaction.Rollback();
            await InsertCsvAuditAsync(options, preview, added, updated, deleted, restored, unchanged, skipped, "Failed", ex.Message);
            throw;
        }

        return new CsvImportApplyResult(added, updated, deleted, restored, unchanged, skipped, preview.InvalidRowCount, backupPath, auditId);
    }

    public async Task DeleteAsync(int id)
    {
        await SoftDeleteAsync(id, DateTime.UtcNow);
    }

    public async Task SoftDeleteAsync(int id, DateTime deletedAtUtc)
    {
        await using var connection = await _connectionFactory.CreateOpenConnectionAsync();
        using var transaction = connection.BeginTransaction();
        await SoftDeleteCoreAsync(connection, transaction, id, deletedAtUtc);
        transaction.Commit();
    }

    public async Task RestoreAsync(int id)
    {
        var now = DateTime.UtcNow;
        await using var connection = await _connectionFactory.CreateOpenConnectionAsync();
        using var transaction = connection.BeginTransaction();
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            UPDATE Devices
            SET IsDeleted = 0,
                IsEnabled = 1,
                IsActive = 1,
                AutoCheckEnabled = 1,
                DeletedAtUtc = NULL,
                UpdatedAt = @UpdatedAt
            WHERE Id = @Id;
            """;
        AddParameter(command, "@UpdatedAt", ToStorageDate(now));
        AddParameter(command, "@Id", id);
        var affected = await command.ExecuteNonQueryAsync();
        if (affected > 0)
        {
            await TransitionAvailabilityAsync(connection, transaction, id, AvailabilityStatus.Unknown, now, "Restore", "Cihaz geri yuklendi; ilk dogrulamaya kadar Unknown.");
        }

        transaction.Commit();
    }

    public async Task<int> BulkSoftDeleteAsync(IEnumerable<int> deviceIds)
    {
        var ids = deviceIds.Where(id => id > 0).Distinct().ToList();
        if (ids.Count == 0)
        {
            return 0;
        }

        var affected = 0;
        var now = DateTime.UtcNow;
        await using var connection = await _connectionFactory.CreateOpenConnectionAsync();
        using var transaction = connection.BeginTransaction();
        foreach (var id in ids)
        {
            affected += await SoftDeleteCoreAsync(connection, transaction, id, now);
        }

        transaction.Commit();
        return affected;
    }

    public async Task<int> BulkSoftDeleteByGroupAsync(int groupId, bool deleteEmptyGroup)
    {
        if (groupId <= 0)
        {
            return 0;
        }

        var now = DateTime.UtcNow;
        var affected = 0;
        await using var connection = await _connectionFactory.CreateOpenConnectionAsync();
        using var transaction = connection.BeginTransaction();

        var ids = new List<int>();
        await using (var select = connection.CreateCommand())
        {
            select.Transaction = transaction;
            select.CommandText = "SELECT Id FROM Devices WHERE GroupId = @GroupId AND IsDeleted = 0;";
            AddParameter(select, "@GroupId", groupId);
            await using var reader = await select.ExecuteReaderAsync();
            while (await reader.ReadAsync())
            {
                ids.Add(reader.GetInt32(0));
            }
        }

        foreach (var id in ids)
        {
            affected += await SoftDeleteCoreAsync(connection, transaction, id, now);
        }

        if (deleteEmptyGroup)
        {
            await using var deleteGroup = connection.CreateCommand();
            deleteGroup.Transaction = transaction;
            deleteGroup.CommandText = """
                DELETE FROM DeviceGroups
                WHERE Id = @GroupId
                  AND NOT EXISTS (
                      SELECT 1
                      FROM Devices
                      WHERE GroupId = @GroupId
                        AND IsDeleted = 0
                  );
                """;
            AddParameter(deleteGroup, "@GroupId", groupId);
            await deleteGroup.ExecuteNonQueryAsync();
        }

        transaction.Commit();
        return affected;
    }

    public async Task<int> BulkRestoreAsync(IEnumerable<int> deviceIds)
    {
        var ids = deviceIds.Where(id => id > 0).Distinct().ToList();
        if (ids.Count == 0)
        {
            return 0;
        }

        var affected = 0;
        var now = DateTime.UtcNow;
        await using var connection = await _connectionFactory.CreateOpenConnectionAsync();
        using var transaction = connection.BeginTransaction();
        foreach (var id in ids)
        {
            await using var command = connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText = """
                UPDATE Devices
                SET IsDeleted = 0,
                    IsEnabled = 1,
                    IsActive = 1,
                    AutoCheckEnabled = 1,
                    DeletedAtUtc = NULL,
                    UpdatedAt = @UpdatedAt
                WHERE Id = @Id
                  AND IsDeleted = 1;
                """;
            AddParameter(command, "@UpdatedAt", ToStorageDate(now));
            AddParameter(command, "@Id", id);
            var restored = await command.ExecuteNonQueryAsync();
            if (restored > 0)
            {
                await TransitionAvailabilityAsync(connection, transaction, id, AvailabilityStatus.Unknown, now, "Restore", "Cihaz geri yuklendi; ilk dogrulamaya kadar Unknown.");
            }

            affected += restored;
        }

        transaction.Commit();
        return affected;
    }

    public async Task<int> BulkSetAutoCheckAsync(IEnumerable<int> deviceIds, bool enabled)
    {
        return await BulkUpdateAsync(
            deviceIds,
            "UPDATE Devices SET AutoCheckEnabled = @Value, UpdatedAt = @UpdatedAt WHERE Id = @Id AND IsDeleted = 0;",
            command => AddParameter(command, "@Value", enabled ? 1 : 0));
    }

    public async Task<int> BulkSetActiveAsync(IEnumerable<int> deviceIds, bool isActive)
    {
        return await BulkUpdateAsync(
            deviceIds,
            "UPDATE Devices SET IsActive = @Value, IsEnabled = @Value, UpdatedAt = @UpdatedAt WHERE Id = @Id AND IsDeleted = 0;",
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
            WHERE Id = @Id AND IsDeleted = 0;
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
            "UPDATE Devices SET CheckIntervalSeconds = @Value, UpdatedAt = @UpdatedAt WHERE Id = @Id AND IsDeleted = 0;",
            command => AddParameter(command, "@Value", normalized));
    }

    public async Task<int> BulkSetSuppressionAsync(
        IEnumerable<int> deviceIds,
        DeviceSuppressionMode mode,
        DateTime? untilUtc,
        string reason,
        string suppressedBy,
        DateTime nowUtc,
        CancellationToken cancellationToken = default)
    {
        if (mode == DeviceSuppressionMode.None)
        {
            return await BulkClearSuppressionAsync(deviceIds, nowUtc, cancellationToken);
        }

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
            command.CommandText = """
                UPDATE Devices
                SET SuppressionMode = @SuppressionMode,
                    SuppressedFromUtc = @SuppressedFromUtc,
                    SuppressedUntilUtc = @SuppressedUntilUtc,
                    SuppressionReason = @SuppressionReason,
                    SuppressedBy = @SuppressedBy,
                    UpdatedAt = @UpdatedAt
                WHERE Id = @Id
                  AND IsDeleted = 0;
                """;
            AddParameter(command, "@SuppressionMode", mode.ToStorageValue());
            AddParameter(command, "@SuppressedFromUtc", ToStorageDate(nowUtc.ToUniversalTime()));
            AddParameter(command, "@SuppressedUntilUtc", untilUtc.HasValue ? ToStorageDate(untilUtc.Value.ToUniversalTime()) : null);
            AddParameter(command, "@SuppressionReason", reason.Trim());
            AddParameter(command, "@SuppressedBy", string.IsNullOrWhiteSpace(suppressedBy) ? Environment.UserName : suppressedBy.Trim());
            AddParameter(command, "@UpdatedAt", ToStorageDate(nowUtc.ToUniversalTime()));
            AddParameter(command, "@Id", id);
            affected += await command.ExecuteNonQueryAsync(cancellationToken);
        }

        transaction.Commit();
        return affected;
    }

    public async Task<int> BulkClearSuppressionAsync(
        IEnumerable<int> deviceIds,
        DateTime nowUtc,
        CancellationToken cancellationToken = default)
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
            await AddSuppressedDurationToOpenIncidentAsync(connection, transaction, id, nowUtc, cancellationToken);

            await using var command = connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText = """
                UPDATE Devices
                SET SuppressionMode = 'None',
                    SuppressedFromUtc = NULL,
                    SuppressedUntilUtc = NULL,
                    SuppressionReason = '',
                    SuppressedBy = '',
                    UpdatedAt = @UpdatedAt
                WHERE Id = @Id
                  AND SuppressionMode <> 'None';
                """;
            AddParameter(command, "@UpdatedAt", ToStorageDate(nowUtc.ToUniversalTime()));
            AddParameter(command, "@Id", id);
            affected += await command.ExecuteNonQueryAsync(cancellationToken);
        }

        transaction.Commit();
        return affected;
    }

    public async Task<int> ExpireSuppressionsAsync(DateTime nowUtc, CancellationToken cancellationToken = default)
    {
        var expiredIds = new List<int>();
        await using var connection = await _connectionFactory.CreateOpenConnectionAsync();
        using var transaction = connection.BeginTransaction();

        await using (var select = connection.CreateCommand())
        {
            select.Transaction = transaction;
            select.CommandText = """
                SELECT Id
                FROM Devices
                WHERE SuppressionMode <> 'None'
                  AND SuppressedUntilUtc IS NOT NULL
                  AND SuppressedUntilUtc <= @NowUtc;
                """;
            AddParameter(select, "@NowUtc", ToStorageDate(nowUtc.ToUniversalTime()));
            await using var reader = await select.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
            {
                expiredIds.Add(reader.GetInt32(0));
            }
        }

        foreach (var id in expiredIds)
        {
            await AddSuppressedDurationToOpenIncidentAsync(connection, transaction, id, nowUtc, cancellationToken);
        }

        await using var update = connection.CreateCommand();
        update.Transaction = transaction;
        update.CommandText = """
            UPDATE Devices
            SET SuppressionMode = 'None',
                SuppressedFromUtc = NULL,
                SuppressedUntilUtc = NULL,
                SuppressionReason = '',
                SuppressedBy = '',
                UpdatedAt = @UpdatedAt
            WHERE SuppressionMode <> 'None'
              AND SuppressedUntilUtc IS NOT NULL
              AND SuppressedUntilUtc <= @NowUtc;
            """;
        AddParameter(update, "@UpdatedAt", ToStorageDate(nowUtc.ToUniversalTime()));
        AddParameter(update, "@NowUtc", ToStorageDate(nowUtc.ToUniversalTime()));
        var affected = await update.ExecuteNonQueryAsync(cancellationToken);
        transaction.Commit();
        return affected;
    }

    private static async Task AddSuppressedDurationToOpenIncidentAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        int deviceId,
        DateTime endedAtUtc,
        CancellationToken cancellationToken)
    {
        long? incidentId = null;
        DateTime? incidentStartedAtUtc = null;
        DateTime? suppressedFromUtc = null;

        await using (var select = connection.CreateCommand())
        {
            select.Transaction = transaction;
            select.CommandText = """
                SELECT i.Id, i.StartedAtUtc, d.SuppressedFromUtc
                FROM DeviceIncidents i
                JOIN Devices d ON d.Id = i.DeviceId
                WHERE i.DeviceId = @DeviceId
                  AND i.Status = 'Open'
                  AND d.SuppressionMode <> 'None'
                LIMIT 1;
                """;
            AddParameter(select, "@DeviceId", deviceId);
            await using var reader = await select.ExecuteReaderAsync(cancellationToken);
            if (await reader.ReadAsync(cancellationToken))
            {
                incidentId = reader.GetInt64(0);
                incidentStartedAtUtc = FromStorageDate(reader.GetString(1)).ToUniversalTime();
                suppressedFromUtc = reader.IsDBNull(2) ? null : FromStorageDate(reader.GetString(2)).ToUniversalTime();
            }
        }

        if (!incidentId.HasValue || !incidentStartedAtUtc.HasValue)
        {
            return;
        }

        var effectiveStart = suppressedFromUtc.HasValue && suppressedFromUtc.Value > incidentStartedAtUtc.Value
            ? suppressedFromUtc.Value
            : incidentStartedAtUtc.Value;
        var seconds = Math.Max(0, (long)(endedAtUtc.ToUniversalTime() - effectiveStart).TotalSeconds);
        if (seconds <= 0)
        {
            return;
        }

        await using var update = connection.CreateCommand();
        update.Transaction = transaction;
        update.CommandText = """
            UPDATE DeviceIncidents
            SET SuppressedDurationSeconds = SuppressedDurationSeconds + @Seconds,
                UpdatedAtUtc = @EndedAtUtc
            WHERE Id = @Id;
            """;
        AddParameter(update, "@Seconds", seconds);
        AddParameter(update, "@EndedAtUtc", ToStorageDate(endedAtUtc.ToUniversalTime()));
        AddParameter(update, "@Id", incidentId.Value);
        await update.ExecuteNonQueryAsync(cancellationToken);
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

    private static async Task<int> SoftDeleteCoreAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        int id,
        DateTime deletedAtUtc)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            UPDATE Devices
            SET IsDeleted = 1,
                IsEnabled = 0,
                IsActive = 0,
                AutoCheckEnabled = 0,
                DeletedAtUtc = @DeletedAtUtc,
                UpdatedAt = @UpdatedAt
            WHERE Id = @Id
              AND IsDeleted = 0;
            """;
        AddParameter(command, "@DeletedAtUtc", ToStorageDate(deletedAtUtc));
        AddParameter(command, "@UpdatedAt", ToStorageDate(deletedAtUtc));
        AddParameter(command, "@Id", id);
        var affected = await command.ExecuteNonQueryAsync();

        if (affected == 0)
        {
            return 0;
        }

        await using var closeIncidents = connection.CreateCommand();
        closeIncidents.Transaction = transaction;
        closeIncidents.CommandText = """
            UPDATE DeviceIncidents
            SET Status = 'Cancelled',
                EndedAtUtc = @ClosedAtUtc,
                UpdatedAtUtc = @ClosedAtUtc
            WHERE DeviceId = @Id
              AND Status = 'Open';
            """;
        AddParameter(closeIncidents, "@ClosedAtUtc", ToStorageDate(deletedAtUtc));
        AddParameter(closeIncidents, "@Id", id);
        await closeIncidents.ExecuteNonQueryAsync();

        await using var closeOutages = connection.CreateCommand();
        closeOutages.Transaction = transaction;
        closeOutages.CommandText = """
            UPDATE Outages
            SET EndedAt = @ClosedAtUtc,
                IsResolved = 1
            WHERE DeviceId = @Id
              AND IsResolved = 0;
            """;
        AddParameter(closeOutages, "@ClosedAtUtc", ToStorageDate(deletedAtUtc));
        AddParameter(closeOutages, "@Id", id);
        await closeOutages.ExecuteNonQueryAsync();

        await using var cancelOutbox = connection.CreateCommand();
        cancelOutbox.Transaction = transaction;
        cancelOutbox.CommandText = """
            UPDATE NotificationOutbox
            SET Status = 'Cancelled',
                CancelledAtUtc = @CancelledAtUtc,
                LockedAtUtc = NULL,
                LockedBy = '',
                LastError = 'Device was soft-deleted before notification was sent.'
            WHERE DeviceId = @Id
              AND Status IN ('Pending','Processing')
              AND EventType IN ('DeviceDown','DeviceRecovered','Test');
            """;
        AddParameter(cancelOutbox, "@CancelledAtUtc", ToStorageDate(deletedAtUtc));
        AddParameter(cancelOutbox, "@Id", id);
        await cancelOutbox.ExecuteNonQueryAsync();

        await TransitionAvailabilityAsync(
            connection,
            transaction,
            id,
            AvailabilityStatus.Paused,
            deletedAtUtc,
            "SoftDelete",
            "Cihaz soft-delete edildi; izleme duraklatildi.");

        return affected;
    }

    private static async Task TransitionAvailabilityAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        int deviceId,
        AvailabilityStatus status,
        DateTime startedAtUtc,
        string reasonCode,
        string reasonText)
    {
        var started = startedAtUtc.ToUniversalTime();
        var now = DateTime.UtcNow;
        long? openId = null;
        DateTime? openStartedAtUtc = null;
        string? openStatus = null;

        await using (var select = connection.CreateCommand())
        {
            select.Transaction = transaction;
            select.CommandText = """
                SELECT Id, Status, StartedAtUtc
                FROM DeviceAvailabilityPeriods
                WHERE DeviceId = @DeviceId
                  AND EndedAtUtc IS NULL
                ORDER BY StartedAtUtc DESC
                LIMIT 1;
                """;
            AddParameter(select, "@DeviceId", deviceId);
            await using var reader = await select.ExecuteReaderAsync();
            if (await reader.ReadAsync())
            {
                openId = reader.GetInt64(0);
                openStatus = reader.GetString(1);
                openStartedAtUtc = FromStorageDate(reader.GetString(2)).ToUniversalTime();
            }
        }

        if (openId.HasValue && string.Equals(openStatus, status.ToStorageValue(), StringComparison.OrdinalIgnoreCase))
        {
            await using var update = connection.CreateCommand();
            update.Transaction = transaction;
            update.CommandText = """
                UPDATE DeviceAvailabilityPeriods
                SET ReasonCode = @ReasonCode,
                    ReasonText = @ReasonText,
                    DetectionSource = 'Management',
                    UpdatedAtUtc = @UpdatedAtUtc
                WHERE Id = @Id;
                """;
            AddParameter(update, "@ReasonCode", reasonCode);
            AddParameter(update, "@ReasonText", reasonText);
            AddParameter(update, "@UpdatedAtUtc", ToStorageDate(now));
            AddParameter(update, "@Id", openId.Value);
            await update.ExecuteNonQueryAsync();
            return;
        }

        if (openId.HasValue && openStartedAtUtc.HasValue)
        {
            var ended = started < openStartedAtUtc.Value ? openStartedAtUtc.Value : started;
            await using var close = connection.CreateCommand();
            close.Transaction = transaction;
            close.CommandText = """
                UPDATE DeviceAvailabilityPeriods
                SET EndedAtUtc = @EndedAtUtc,
                    DurationSeconds = @DurationSeconds,
                    UpdatedAtUtc = @UpdatedAtUtc
                WHERE Id = @Id
                  AND EndedAtUtc IS NULL;
                """;
            AddParameter(close, "@EndedAtUtc", ToStorageDate(ended));
            AddParameter(close, "@DurationSeconds", Math.Max(0, (long)(ended - openStartedAtUtc.Value).TotalSeconds));
            AddParameter(close, "@UpdatedAtUtc", ToStorageDate(now));
            AddParameter(close, "@Id", openId.Value);
            await close.ExecuteNonQueryAsync();
        }

        await using var insert = connection.CreateCommand();
        insert.Transaction = transaction;
        insert.CommandText = """
            INSERT INTO DeviceAvailabilityPeriods
                (DeviceId, Status, StartedAtUtc, EndedAtUtc, DurationSeconds, IncidentId,
                 ReasonCode, ReasonText, DetectionSource, FirstFailureAtUtc, ConfirmedAtUtc,
                 CreatedAtUtc, UpdatedAtUtc)
            VALUES
                (@DeviceId, @Status, @StartedAtUtc, NULL, NULL, NULL,
                 @ReasonCode, @ReasonText, 'Management', NULL, NULL,
                 @CreatedAtUtc, @UpdatedAtUtc);
            """;
        AddParameter(insert, "@DeviceId", deviceId);
        AddParameter(insert, "@Status", status.ToStorageValue());
        AddParameter(insert, "@StartedAtUtc", ToStorageDate(started));
        AddParameter(insert, "@ReasonCode", reasonCode);
        AddParameter(insert, "@ReasonText", reasonText);
        AddParameter(insert, "@CreatedAtUtc", ToStorageDate(now));
        AddParameter(insert, "@UpdatedAtUtc", ToStorageDate(now));
        await insert.ExecuteNonQueryAsync();
    }

    private static async Task InsertDeviceFromCsvAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        DeviceCsvRecord row,
        DateTime nowUtc)
    {
        var groupId = await EnsureGroupIdAsync(connection, transaction, row.GroupName, null);
        await using var insert = connection.CreateCommand();
        insert.Transaction = transaction;
        insert.CommandText = """
            INSERT INTO Devices
                (Name, IpAddress, DeviceType, Location, GroupId, GroupName, IsCritical, IsActive,
                 IsEnabled, IsDeleted, DeletedAtUtc, AutoCheckEnabled, DefaultSchedulePlanId, PingTimeoutMs,
                 CheckIntervalSeconds, FailureRetryIntervalSeconds, FailureRetryLimit, FailureThreshold,
                 Description, LastStatus, LastLatencyMs, LastCheckedAt, LastSuccessfulCheckAt, LastFailedCheckAt,
                 ConsecutiveFailures, ConsecutiveSuccesses, LastStableStatus, CreatedAt, UpdatedAt)
            VALUES
                (@Name, @IpAddress, @DeviceType, @Location, @GroupId, @GroupName, @IsCritical, @IsActive,
                 @IsEnabled, 0, NULL, @AutoCheckEnabled, NULL, @PingTimeoutMs,
                 @CheckIntervalSeconds, @FailureRetryIntervalSeconds, @FailureRetryLimit, @FailureThreshold,
                 @Description, @LastStatus, NULL, NULL, NULL, NULL, 0, 0, @LastStableStatus, @CreatedAt, @UpdatedAt);
            """;
        AddCsvDeviceParameters(insert, row, groupId, nowUtc);
        await insert.ExecuteNonQueryAsync();
    }

    private static async Task UpdateDeviceFromCsvByIpAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        DeviceCsvRecord row,
        bool restore,
        DateTime nowUtc)
    {
        var groupId = await EnsureGroupIdAsync(connection, transaction, row.GroupName, null);
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
                IsActive = @IsActive,
                IsEnabled = @IsEnabled,
                IsDeleted = 0,
                DeletedAtUtc = NULL,
                AutoCheckEnabled = @AutoCheckEnabled,
                PingTimeoutMs = @PingTimeoutMs,
                CheckIntervalSeconds = @CheckIntervalSeconds,
                FailureRetryIntervalSeconds = @FailureRetryIntervalSeconds,
                FailureRetryLimit = @FailureRetryLimit,
                FailureThreshold = @FailureThreshold,
                Description = @Description,
                UpdatedAt = @UpdatedAt
            WHERE IpAddress = @IpAddress
              AND (@Restore = 0 OR IsDeleted = 1);
            """;
        AddCsvDeviceParameters(update, row, groupId, nowUtc);
        AddParameter(update, "@Restore", restore ? 1 : 0);
        var affected = await update.ExecuteNonQueryAsync();
        if (affected == 0)
        {
            throw new InvalidOperationException($"CSV import target not found for IP {row.IpAddress}.");
        }
    }

    private static void AddCsvDeviceParameters(SqliteCommand command, DeviceCsvRecord row, int? groupId, DateTime nowUtc)
    {
        AddParameter(command, "@Name", row.Name.Trim());
        AddParameter(command, "@IpAddress", row.IpAddress.Trim());
        AddParameter(command, "@DeviceType", row.DeviceType.ToStorageValue());
        AddParameter(command, "@Location", row.Location.Trim());
        AddParameter(command, "@GroupId", groupId);
        AddParameter(command, "@GroupName", row.GroupName.Trim());
        AddParameter(command, "@IsCritical", row.IsCritical ? 1 : 0);
        AddParameter(command, "@IsActive", row.IsEnabled ? 1 : 0);
        AddParameter(command, "@IsEnabled", row.IsEnabled ? 1 : 0);
        AddParameter(command, "@AutoCheckEnabled", row.AutoCheckEnabled ? 1 : 0);
        AddParameter(command, "@PingTimeoutMs", row.PingTimeoutMs);
        AddParameter(command, "@CheckIntervalSeconds", row.CheckIntervalSeconds <= 0 ? 0 : Math.Clamp(row.CheckIntervalSeconds, AppSettings.MinDeviceCheckIntervalSeconds, AppSettings.MaxDeviceCheckIntervalSeconds));
        AddParameter(command, "@FailureRetryIntervalSeconds", row.RetryIntervalSeconds <= 0 ? 0 : Math.Clamp(row.RetryIntervalSeconds, AppSettings.MinFailureRetryIntervalSeconds, AppSettings.MaxFailureRetryIntervalSeconds));
        AddParameter(command, "@FailureRetryLimit", row.RetryLimit <= 0 ? 0 : Math.Clamp(row.RetryLimit, AppSettings.MinFailureRetryLimit, AppSettings.MaxFailureRetryLimit));
        AddParameter(command, "@FailureThreshold", row.FailureThreshold <= 0 ? 0 : Math.Clamp(row.FailureThreshold, AppSettings.MinFailureThreshold, AppSettings.MaxFailureThreshold));
        AddParameter(command, "@Description", row.Description.Trim());
        AddParameter(command, "@LastStatus", DeviceStatus.Unknown.ToStorageValue());
        AddParameter(command, "@LastStableStatus", DeviceStatus.Unknown.ToStorageValue());
        AddParameter(command, "@CreatedAt", ToStorageDate(nowUtc));
        AddParameter(command, "@UpdatedAt", ToStorageDate(nowUtc));
    }

    private async Task<long> InsertCsvAuditAsync(
        CsvImportOptions options,
        CsvImportPreview preview,
        int added,
        int updated,
        int deleted,
        int restored,
        int unchanged,
        int skipped,
        string result,
        string error)
    {
        await using var connection = await _connectionFactory.CreateOpenConnectionAsync();
        using var transaction = connection.BeginTransaction();
        var id = await InsertCsvAuditAsync(connection, transaction, options, preview, added, updated, deleted, restored, unchanged, skipped, result, error);
        transaction.Commit();
        return id;
    }

    private static async Task<long> InsertCsvAuditAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        CsvImportOptions options,
        CsvImportPreview preview,
        int added,
        int updated,
        int deleted,
        int restored,
        int unchanged,
        int skipped,
        string result,
        string error)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            INSERT INTO CsvImportAudits
                (ImportedAtUtc, FileName, ImportMode, ImportScope, AddedCount, UpdatedCount, DeletedCount,
                 RestoredCount, UnchangedCount, SkippedCount, InvalidRowCount, DuplicateRowCount,
                 InitiatedBy, Result, ErrorMessage)
            VALUES
                (@ImportedAtUtc, @FileName, @ImportMode, @ImportScope, @AddedCount, @UpdatedCount, @DeletedCount,
                 @RestoredCount, @UnchangedCount, @SkippedCount, @InvalidRowCount, @DuplicateRowCount,
                 @InitiatedBy, @Result, @ErrorMessage);
            SELECT last_insert_rowid();
            """;
        AddParameter(command, "@ImportedAtUtc", ToStorageDate(DateTime.UtcNow));
        AddParameter(command, "@FileName", options.FileName);
        AddParameter(command, "@ImportMode", options.Mode.ToString());
        AddParameter(command, "@ImportScope", options.Scope == CsvImportScope.SelectedGroup ? $"Group:{options.GroupName}" : "AllActiveDevices");
        AddParameter(command, "@AddedCount", added);
        AddParameter(command, "@UpdatedCount", updated);
        AddParameter(command, "@DeletedCount", deleted);
        AddParameter(command, "@RestoredCount", restored);
        AddParameter(command, "@UnchangedCount", unchanged);
        AddParameter(command, "@SkippedCount", skipped);
        AddParameter(command, "@InvalidRowCount", preview.InvalidRowCount);
        AddParameter(command, "@DuplicateRowCount", preview.DuplicateCount);
        AddParameter(command, "@InitiatedBy", options.InitiatedBy);
        AddParameter(command, "@Result", result);
        AddParameter(command, "@ErrorMessage", error);
        var value = await command.ExecuteScalarAsync();
        return Convert.ToInt64(value, CultureInfo.InvariantCulture);
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
        command.CommandText = "SELECT COUNT(1) FROM Devices WHERE GroupId = @GroupId AND IsDeleted = 0;";
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
            IsEnabled = reader.GetInt32(9) == 1,
            IsDeleted = reader.GetInt32(10) == 1,
            DeletedAtUtc = reader.IsDBNull(11) ? null : FromStorageDate(reader.GetString(11)),
            AutoCheckEnabled = reader.GetInt32(12) == 1,
            DefaultSchedulePlanId = reader.IsDBNull(13) ? null : reader.GetInt32(13),
            PingTimeoutMs = reader.IsDBNull(14) ? null : reader.GetInt32(14),
            CheckIntervalSeconds = reader.GetInt32(15),
            FailureRetryIntervalSeconds = reader.GetInt32(16),
            FailureRetryLimit = reader.GetInt32(17),
            FailureThreshold = reader.GetInt32(18),
            Description = reader.GetString(19),
            LastStatus = DeviceStatusExtensions.FromStorageValue(reader.GetString(20)),
            LastLatencyMs = reader.IsDBNull(21) ? null : reader.GetInt64(21),
            LastCheckedAt = reader.IsDBNull(22) ? null : FromStorageDate(reader.GetString(22)),
            LastSuccessfulCheckAt = reader.IsDBNull(23) ? null : FromStorageDate(reader.GetString(23)),
            LastFailedCheckAt = reader.IsDBNull(24) ? null : FromStorageDate(reader.GetString(24)),
            ConsecutiveFailures = reader.GetInt32(25),
            ConsecutiveSuccesses = reader.GetInt32(26),
            LastStableStatus = DeviceStatusExtensions.FromStorageValue(reader.GetString(27)),
            SuppressionMode = DeviceSuppressionModeExtensions.FromStorageValue(reader.GetString(28)),
            SuppressedFromUtc = reader.IsDBNull(29) ? null : FromStorageDate(reader.GetString(29)),
            SuppressedUntilUtc = reader.IsDBNull(30) ? null : FromStorageDate(reader.GetString(30)),
            SuppressionReason = reader.GetString(31),
            SuppressedBy = reader.GetString(32),
            CreatedAt = FromStorageDate(reader.GetString(33)),
            UpdatedAt = FromStorageDate(reader.GetString(34)),
            SlaTargetAvailabilityPercent = reader.IsDBNull(35) ? null : reader.GetDouble(35)
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
        AddParameter(command, "@IsEnabled", device.IsEnabled ? 1 : 0);
        AddParameter(command, "@IsDeleted", device.IsDeleted ? 1 : 0);
        AddParameter(command, "@DeletedAtUtc", device.DeletedAtUtc.HasValue ? ToStorageDate(device.DeletedAtUtc.Value) : null);
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
        AddParameter(command, "@SuppressionMode", device.SuppressionMode.ToStorageValue());
        AddParameter(command, "@SuppressedFromUtc", device.SuppressedFromUtc.HasValue ? ToStorageDate(device.SuppressedFromUtc.Value.ToUniversalTime()) : null);
        AddParameter(command, "@SuppressedUntilUtc", device.SuppressedUntilUtc.HasValue ? ToStorageDate(device.SuppressedUntilUtc.Value.ToUniversalTime()) : null);
        AddParameter(command, "@SuppressionReason", device.SuppressionReason.Trim());
        AddParameter(command, "@SuppressedBy", device.SuppressedBy.Trim());
        AddParameter(command, "@CreatedAt", ToStorageDate(device.CreatedAt));
        AddParameter(command, "@UpdatedAt", ToStorageDate(device.UpdatedAt));
        AddParameter(command, "@TargetAvailabilityPercent", device.SlaTargetAvailabilityPercent);
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
