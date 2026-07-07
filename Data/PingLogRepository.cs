using System.Globalization;
using Microsoft.Data.Sqlite;
using NetworkHealthMonitor.Models;

namespace NetworkHealthMonitor.Data;

public sealed class PingLogRepository
{
    private readonly SqliteConnectionFactory _connectionFactory;

    public PingLogRepository(SqliteConnectionFactory connectionFactory)
    {
        _connectionFactory = connectionFactory;
    }

    public async Task<int> AddAsync(PingLog log)
    {
        await using var connection = await _connectionFactory.CreateOpenConnectionAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = InsertSql + " SELECT last_insert_rowid();";

        AddLogParameters(command, log);
        var result = await command.ExecuteScalarAsync();
        var id = Convert.ToInt32(result, CultureInfo.InvariantCulture);
        log.Id = id;
        return id;
    }

    public async Task AddRangeAsync(IEnumerable<PingLog> logs)
    {
        var materialized = logs.ToList();
        if (materialized.Count == 0)
        {
            return;
        }

        await using var connection = await _connectionFactory.CreateOpenConnectionAsync();
        using var transaction = connection.BeginTransaction();

        foreach (var log in materialized)
        {
            await using var command = connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText = InsertSql + " SELECT last_insert_rowid();";

            AddLogParameters(command, log);
            var result = await command.ExecuteScalarAsync();
            log.Id = Convert.ToInt32(result, CultureInfo.InvariantCulture);
        }

        transaction.Commit();
    }

    public async Task<IReadOnlyList<PingLog>> GetRecentAsync(int limit = 5000)
    {
        return await GetFilteredAsync(
            null,
            null,
            null,
            null,
            null,
            null,
            null,
            null,
            null,
            false,
            limit);
    }

    public async Task<IReadOnlyDictionary<int, DeviceHealthMetrics>> GetDeviceHealthMetricsAsync(DateTime since)
    {
        var rows = await GetForAvailabilityAsync(since);
        var now = DateTime.Now;
        return rows
            .Where(row => row.DeviceId.HasValue)
            .GroupBy(row => row.DeviceId!.Value)
            .ToDictionary(
                group => group.Key,
                group =>
                {
                    var materialized = group.ToList();
                    return new DeviceHealthMetrics
                    {
                        DeviceId = group.Key,
                        Uptime24HoursPercent = CalculateUptime(materialized, now.AddHours(-24)),
                        Uptime7DaysPercent = CalculateUptime(materialized, now.AddDays(-7)),
                        Uptime30DaysPercent = CalculateUptime(materialized, now.AddDays(-30)),
                        AverageLatencyMs = CalculateAverageLatency(materialized),
                        LastFailureAt = materialized
                            .Where(row => row.Status == DeviceStatus.Unreachable)
                            .OrderByDescending(row => row.CheckedAt)
                            .Select(row => (DateTime?)row.CheckedAt)
                            .FirstOrDefault(),
                        FailureCount30Days = materialized.Count(row => row.Status == DeviceStatus.Unreachable),
                        TotalChecks30Days = materialized.Count
                    };
                });
    }

    public async Task<IReadOnlyList<PingLog>> GetForAvailabilityAsync(DateTime since)
    {
        var logs = new List<PingLog>();
        await using var connection = await _connectionFactory.CreateOpenConnectionAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT Id, DeviceId, DeviceName, IpAddress, DeviceType, GroupName, Status, LatencyMs,
                   ResponseMessage, ErrorMessage, CheckedAt, TriggerType, SchedulePlanId, SchedulePlanName
            FROM PingLogs
            WHERE DeviceId IS NOT NULL
              AND CheckedAt >= @Since
            ORDER BY CheckedAt DESC;
            """;

        AddParameter(command, "@Since", ToStorageDate(since));
        await using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            logs.Add(ReadLog(reader));
        }

        return logs;
    }

    public async Task<IReadOnlyList<PingLog>> GetFilteredAsync(
        DateTime? startDate,
        DateTime? endDate,
        string? deviceName,
        string? ipAddress,
        DeviceType? deviceType,
        DeviceStatus? status,
        bool onlyUnreachable,
        int limit = 5000)
    {
        return await GetFilteredAsync(
            startDate,
            endDate,
            deviceName,
            ipAddress,
            deviceType,
            status,
            null,
            null,
            null,
            onlyUnreachable,
            limit);
    }

    public async Task<IReadOnlyList<PingLog>> GetFilteredAsync(
        DateTime? startDate,
        DateTime? endDate,
        string? deviceName,
        string? ipAddress,
        DeviceType? deviceType,
        DeviceStatus? status,
        string? groupName,
        PingTriggerType? triggerType,
        string? planName,
        bool onlyUnreachable,
        int limit = 5000)
    {
        var logs = new List<PingLog>();
        await using var connection = await _connectionFactory.CreateOpenConnectionAsync();
        await using var command = connection.CreateCommand();

        var where = new List<string>();
        if (startDate.HasValue)
        {
            where.Add("CheckedAt >= @StartDate");
            AddParameter(command, "@StartDate", ToStorageDate(startDate.Value.Date));
        }

        if (endDate.HasValue)
        {
            where.Add("CheckedAt < @EndDate");
            AddParameter(command, "@EndDate", ToStorageDate(endDate.Value.Date.AddDays(1)));
        }

        if (!string.IsNullOrWhiteSpace(deviceName))
        {
            where.Add("DeviceName LIKE @DeviceName");
            AddParameter(command, "@DeviceName", $"%{deviceName.Trim()}%");
        }

        if (!string.IsNullOrWhiteSpace(ipAddress))
        {
            where.Add("IpAddress LIKE @IpAddress");
            AddParameter(command, "@IpAddress", $"%{ipAddress.Trim()}%");
        }

        if (deviceType.HasValue)
        {
            where.Add("DeviceType = @DeviceType");
            AddParameter(command, "@DeviceType", deviceType.Value.ToStorageValue());
        }

        if (status.HasValue)
        {
            where.Add("Status = @Status");
            AddParameter(command, "@Status", status.Value.ToStorageValue());
        }

        if (!string.IsNullOrWhiteSpace(groupName))
        {
            where.Add("GroupName LIKE @GroupName");
            AddParameter(command, "@GroupName", $"%{groupName.Trim()}%");
        }

        if (triggerType.HasValue)
        {
            where.Add("TriggerType = @TriggerType");
            AddParameter(command, "@TriggerType", triggerType.Value.ToStorageValue());
        }

        if (!string.IsNullOrWhiteSpace(planName))
        {
            where.Add("SchedulePlanName LIKE @SchedulePlanName");
            AddParameter(command, "@SchedulePlanName", $"%{planName.Trim()}%");
        }

        if (onlyUnreachable)
        {
            where.Add("Status = @OnlyUnreachableStatus");
            AddParameter(command, "@OnlyUnreachableStatus", DeviceStatus.Unreachable.ToStorageValue());
        }

        AddParameter(command, "@Limit", Math.Clamp(limit, 1, 50000));
        command.CommandText = $"""
            SELECT Id, DeviceId, DeviceName, IpAddress, DeviceType, GroupName, Status, LatencyMs,
                   ResponseMessage, ErrorMessage, CheckedAt, TriggerType, SchedulePlanId, SchedulePlanName
            FROM PingLogs
            {(where.Count == 0 ? string.Empty : "WHERE " + string.Join(" AND ", where))}
            ORDER BY CheckedAt DESC
            LIMIT @Limit;
            """;

        await using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            logs.Add(ReadLog(reader));
        }

        return logs;
    }

    public async Task<int> CountFailuresSinceAsync(DateTime since)
    {
        await using var connection = await _connectionFactory.CreateOpenConnectionAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT COUNT(1)
            FROM PingLogs
            WHERE Status = @Status
              AND CheckedAt >= @Since;
            """;

        AddParameter(command, "@Status", DeviceStatus.Unreachable.ToStorageValue());
        AddParameter(command, "@Since", ToStorageDate(since));
        var result = await command.ExecuteScalarAsync();
        return Convert.ToInt32(result, CultureInfo.InvariantCulture);
    }

    public async Task ClearAsync()
    {
        await using var connection = await _connectionFactory.CreateOpenConnectionAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = "DELETE FROM PingLogs;";
        await command.ExecuteNonQueryAsync();
    }

    public async Task<int> ClearOlderThanAsync(DateTime threshold)
    {
        await using var connection = await _connectionFactory.CreateOpenConnectionAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = "DELETE FROM PingLogs WHERE CheckedAt < @Threshold;";
        AddParameter(command, "@Threshold", ToStorageDate(threshold));
        return await command.ExecuteNonQueryAsync();
    }

    private const string InsertSql = """
        INSERT INTO PingLogs
            (DeviceId, DeviceName, IpAddress, DeviceType, GroupName, Status, LatencyMs,
             ResponseMessage, ErrorMessage, CheckedAt, TriggerType, SchedulePlanId, SchedulePlanName)
        VALUES
            (@DeviceId, @DeviceName, @IpAddress, @DeviceType, @GroupName, @Status, @LatencyMs,
             @ResponseMessage, @ErrorMessage, @CheckedAt, @TriggerType, @SchedulePlanId, @SchedulePlanName);
        """;

    private static PingLog ReadLog(SqliteDataReader reader)
    {
        return new PingLog
        {
            Id = reader.GetInt32(0),
            DeviceId = reader.IsDBNull(1) ? null : reader.GetInt32(1),
            DeviceName = reader.GetString(2),
            IpAddress = reader.GetString(3),
            DeviceType = DeviceTypeExtensions.FromStorageValue(reader.GetString(4)),
            GroupName = reader.GetString(5),
            Status = DeviceStatusExtensions.FromStorageValue(reader.GetString(6)),
            LatencyMs = reader.IsDBNull(7) ? null : reader.GetInt64(7),
            ResponseMessage = reader.GetString(8),
            ErrorMessage = reader.GetString(9),
            CheckedAt = FromStorageDate(reader.GetString(10)),
            TriggerType = PingTriggerTypeExtensions.FromStorageValue(reader.GetString(11)),
            SchedulePlanId = reader.IsDBNull(12) ? null : reader.GetInt32(12),
            SchedulePlanName = reader.GetString(13)
        };
    }

    private static double? CalculateUptime(IReadOnlyCollection<PingLog> rows, DateTime since)
    {
        var scoped = rows.Where(row => row.CheckedAt >= since).ToList();
        if (scoped.Count == 0)
        {
            return null;
        }

        return scoped.Count(row => row.Status == DeviceStatus.Reachable) * 100d / scoped.Count;
    }

    private static long? CalculateAverageLatency(IReadOnlyCollection<PingLog> rows)
    {
        var latencies = rows
            .Where(row => row.Status == DeviceStatus.Reachable && row.LatencyMs.HasValue)
            .Select(row => row.LatencyMs!.Value)
            .ToList();

        return latencies.Count == 0 ? null : (long)Math.Round(latencies.Average());
    }

    private static void AddLogParameters(SqliteCommand command, PingLog log)
    {
        AddParameter(command, "@DeviceId", log.DeviceId);
        AddParameter(command, "@DeviceName", log.DeviceName);
        AddParameter(command, "@IpAddress", log.IpAddress);
        AddParameter(command, "@DeviceType", log.DeviceType.ToStorageValue());
        AddParameter(command, "@GroupName", log.GroupName);
        AddParameter(command, "@Status", log.Status.ToStorageValue());
        AddParameter(command, "@LatencyMs", log.LatencyMs);
        AddParameter(command, "@ResponseMessage", log.ResponseMessage);
        AddParameter(command, "@ErrorMessage", log.ErrorMessage);
        AddParameter(command, "@CheckedAt", ToStorageDate(log.CheckedAt));
        AddParameter(command, "@TriggerType", log.TriggerType.ToStorageValue());
        AddParameter(command, "@SchedulePlanId", log.SchedulePlanId);
        AddParameter(command, "@SchedulePlanName", log.SchedulePlanName);
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
