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
        var now = DateTime.Now;
        var metrics = new Dictionary<int, DeviceHealthMetrics>();
        await using var connection = await _connectionFactory.CreateOpenConnectionAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = $"""
            SELECT DeviceId,
                   SUM(CASE WHEN CheckedAt >= @Since24 AND Status IN ({MeasuredStatusSql}) THEN 1 ELSE 0 END) AS Total24,
                   SUM(CASE WHEN CheckedAt >= @Since24 AND Status IN ({SuccessStatusSql}) THEN 1 ELSE 0 END) AS Success24,
                   SUM(CASE WHEN CheckedAt >= @Since7 AND Status IN ({MeasuredStatusSql}) THEN 1 ELSE 0 END) AS Total7,
                   SUM(CASE WHEN CheckedAt >= @Since7 AND Status IN ({SuccessStatusSql}) THEN 1 ELSE 0 END) AS Success7,
                   SUM(CASE WHEN CheckedAt >= @Since30 AND Status IN ({MeasuredStatusSql}) THEN 1 ELSE 0 END) AS Total30,
                   SUM(CASE WHEN CheckedAt >= @Since30 AND Status IN ({SuccessStatusSql}) THEN 1 ELSE 0 END) AS Success30,
                   SUM(CASE WHEN Status IN ({MeasuredStatusSql}) THEN 1 ELSE 0 END) AS TotalAll,
                   SUM(CASE WHEN Status IN ({SuccessStatusSql}) THEN 1 ELSE 0 END) AS SuccessAll,
                   AVG(CASE WHEN Status IN ({SuccessStatusSql}) AND LatencyMs IS NOT NULL THEN LatencyMs ELSE NULL END) AS AverageLatency,
                   MAX(CASE WHEN Status IN ({FailureStatusSql}) THEN CheckedAt ELSE NULL END) AS LastFailureAt,
                   SUM(CASE WHEN CheckedAt >= @Since30 AND Status IN ({FailureStatusSql}) THEN 1 ELSE 0 END) AS FailureCount30
            FROM PingLogs
            WHERE DeviceId IS NOT NULL
            GROUP BY DeviceId;
            """;

        AddParameter(command, "@Since24", ToStorageDate(now.AddHours(-24)));
        AddParameter(command, "@Since7", ToStorageDate(now.AddDays(-7)));
        AddParameter(command, "@Since30", ToStorageDate(since));

        await using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            var total24 = ReadInt32(reader, 1);
            var success24 = ReadInt32(reader, 2);
            var total7 = ReadInt32(reader, 3);
            var success7 = ReadInt32(reader, 4);
            var total30 = ReadInt32(reader, 5);
            var success30 = ReadInt32(reader, 6);
            var totalAll = ReadInt32(reader, 7);
            var successAll = ReadInt32(reader, 8);
            var averageLatency = reader.IsDBNull(9) ? null : (long?)Math.Round(reader.GetDouble(9));
            var lastFailureAt = reader.IsDBNull(10) ? null : (DateTime?)FromStorageDate(reader.GetString(10));
            var failureCount30 = ReadInt32(reader, 11);
            var deviceId = reader.GetInt32(0);

            metrics[deviceId] = new DeviceHealthMetrics
            {
                DeviceId = deviceId,
                Uptime24HoursPercent = CalculateUptime(success24, total24),
                Uptime7DaysPercent = CalculateUptime(success7, total7),
                Uptime30DaysPercent = CalculateUptime(success30, total30),
                UptimeOverallPercent = CalculateUptime(successAll, totalAll),
                AverageLatencyMs = averageLatency,
                LastFailureAt = lastFailureAt,
                FailureCount30Days = failureCount30,
                TotalChecks30Days = total30
            };
        }

        return metrics;
    }

    public async Task<IReadOnlyDictionary<int, DeviceAvailabilityMetrics>> GetAvailabilityMetricsAsync(DateTime since)
    {
        var now = DateTime.Now;
        var metrics = new Dictionary<int, DeviceAvailabilityMetrics>();
        await using var connection = await _connectionFactory.CreateOpenConnectionAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = $"""
            SELECT DeviceId,
                   SUM(CASE WHEN CheckedAt >= @Since AND Status IN ({SuccessStatusSql}) THEN 1 ELSE 0 END) AS SuccessSince,
                   SUM(CASE WHEN CheckedAt >= @Since AND Status IN ({FailureStatusSql}) THEN 1 ELSE 0 END) AS FailureSince,
                   SUM(CASE WHEN CheckedAt >= @Since24 AND Status IN ({MeasuredStatusSql}) THEN 1 ELSE 0 END) AS Total24,
                   SUM(CASE WHEN CheckedAt >= @Since24 AND Status IN ({SuccessStatusSql}) THEN 1 ELSE 0 END) AS Success24,
                   SUM(CASE WHEN CheckedAt >= @Since7 AND Status IN ({MeasuredStatusSql}) THEN 1 ELSE 0 END) AS Total7,
                   SUM(CASE WHEN CheckedAt >= @Since7 AND Status IN ({SuccessStatusSql}) THEN 1 ELSE 0 END) AS Success7,
                   SUM(CASE WHEN CheckedAt >= @Since30 AND Status IN ({MeasuredStatusSql}) THEN 1 ELSE 0 END) AS Total30,
                   SUM(CASE WHEN CheckedAt >= @Since30 AND Status IN ({SuccessStatusSql}) THEN 1 ELSE 0 END) AS Success30,
                   SUM(CASE WHEN Status IN ({MeasuredStatusSql}) THEN 1 ELSE 0 END) AS TotalAll,
                   SUM(CASE WHEN Status IN ({SuccessStatusSql}) THEN 1 ELSE 0 END) AS SuccessAll,
                   MAX(CASE WHEN Status IN ({SuccessStatusSql}) THEN CheckedAt ELSE NULL END) AS LastSuccessfulCheckAt,
                   MAX(CASE WHEN Status IN ({FailureStatusSql}) THEN CheckedAt ELSE NULL END) AS LastFailedCheckAt
            FROM PingLogs
            WHERE DeviceId IS NOT NULL
            GROUP BY DeviceId;
            """;

        AddParameter(command, "@Since", ToStorageDate(since));
        AddParameter(command, "@Since24", ToStorageDate(now.AddHours(-24)));
        AddParameter(command, "@Since7", ToStorageDate(now.AddDays(-7)));
        AddParameter(command, "@Since30", ToStorageDate(now.AddDays(-30)));

        await using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            var deviceId = reader.GetInt32(0);
            var successSince = ReadInt32(reader, 1);
            var failureSince = ReadInt32(reader, 2);
            var total24 = ReadInt32(reader, 3);
            var success24 = ReadInt32(reader, 4);
            var total7 = ReadInt32(reader, 5);
            var success7 = ReadInt32(reader, 6);
            var total30 = ReadInt32(reader, 7);
            var success30 = ReadInt32(reader, 8);
            var totalAll = ReadInt32(reader, 9);
            var successAll = ReadInt32(reader, 10);
            var lastSuccessfulCheckAt = reader.IsDBNull(11) ? null : (DateTime?)FromStorageDate(reader.GetString(11));
            var lastFailedCheckAt = reader.IsDBNull(12) ? null : (DateTime?)FromStorageDate(reader.GetString(12));

            metrics[deviceId] = new DeviceAvailabilityMetrics
            {
                DeviceId = deviceId,
                TotalSuccessCount = successSince,
                TotalFailureCount = failureSince,
                MeasuredAvailabilityPercent = CalculateUptime(successSince, successSince + failureSince),
                Availability24HoursPercent = CalculateUptime(success24, total24),
                Availability7DaysPercent = CalculateUptime(success7, total7),
                Availability30DaysPercent = CalculateUptime(success30, total30),
                AvailabilityOverallPercent = CalculateUptime(successAll, totalAll),
                LastSuccessfulCheckAt = lastSuccessfulCheckAt,
                LastFailedCheckAt = lastFailedCheckAt
            };
        }

        return metrics;
    }

    public async Task<IReadOnlyList<UptimeReportItem>> GetUptimeReportAsync(IEnumerable<int>? deviceIds = null)
    {
        var ids = deviceIds?
            .Where(id => id > 0)
            .Distinct()
            .ToList();

        if (ids is { Count: 0 })
        {
            return Array.Empty<UptimeReportItem>();
        }

        var now = DateTime.Now;
        var idParameterNames = ids is null
            ? Array.Empty<string>()
            : ids.Select((_, index) => $"@DeviceId{index}").ToArray();
        var logDeviceFilterSql = idParameterNames.Length == 0
            ? string.Empty
            : $"AND DeviceId IN ({string.Join(", ", idParameterNames)})";
        var deviceFilterSql = idParameterNames.Length == 0
            ? string.Empty
            : $"WHERE d.Id IN ({string.Join(", ", idParameterNames)})";

        var items = new List<UptimeReportItem>();
        await using var connection = await _connectionFactory.CreateOpenConnectionAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = $"""
            WITH LogMetrics AS (
                SELECT DeviceId,
                       SUM(CASE WHEN CheckedAt >= @Since24 AND Status IN ({MeasuredStatusSql}) THEN 1 ELSE 0 END) AS Total24,
                       SUM(CASE WHEN CheckedAt >= @Since24 AND Status IN ({SuccessStatusSql}) THEN 1 ELSE 0 END) AS Success24,
                       SUM(CASE WHEN CheckedAt >= @Since24 AND Status IN ({FailureStatusSql}) THEN 1 ELSE 0 END) AS Failure24,
                       SUM(CASE WHEN CheckedAt >= @Since7 AND Status IN ({MeasuredStatusSql}) THEN 1 ELSE 0 END) AS Total7,
                       SUM(CASE WHEN CheckedAt >= @Since7 AND Status IN ({SuccessStatusSql}) THEN 1 ELSE 0 END) AS Success7,
                       SUM(CASE WHEN CheckedAt >= @Since7 AND Status IN ({FailureStatusSql}) THEN 1 ELSE 0 END) AS Failure7,
                       SUM(CASE WHEN CheckedAt >= @Since30 AND Status IN ({MeasuredStatusSql}) THEN 1 ELSE 0 END) AS Total30,
                       SUM(CASE WHEN CheckedAt >= @Since30 AND Status IN ({SuccessStatusSql}) THEN 1 ELSE 0 END) AS Success30,
                       SUM(CASE WHEN CheckedAt >= @Since30 AND Status IN ({FailureStatusSql}) THEN 1 ELSE 0 END) AS Failure30,
                       SUM(CASE WHEN Status IN ({MeasuredStatusSql}) THEN 1 ELSE 0 END) AS TotalAll,
                       SUM(CASE WHEN Status IN ({SuccessStatusSql}) THEN 1 ELSE 0 END) AS SuccessAll,
                       SUM(CASE WHEN Status IN ({FailureStatusSql}) THEN 1 ELSE 0 END) AS FailureAll
                FROM PingLogs
                WHERE DeviceId IS NOT NULL
                  {logDeviceFilterSql}
                GROUP BY DeviceId
            )
            SELECT d.Id,
                   d.Name,
                   d.IpAddress,
                   d.DeviceType,
                   d.GroupName,
                   d.LastStatus,
                   d.LastCheckedAt,
                   d.LastSuccessfulCheckAt,
                   d.LastFailedCheckAt,
                   d.LastLatencyMs,
                   d.ConsecutiveFailures,
                   COALESCE(m.Total24, 0),
                   COALESCE(m.Success24, 0),
                   COALESCE(m.Failure24, 0),
                   COALESCE(m.Total7, 0),
                   COALESCE(m.Success7, 0),
                   COALESCE(m.Failure7, 0),
                   COALESCE(m.Total30, 0),
                   COALESCE(m.Success30, 0),
                   COALESCE(m.Failure30, 0),
                   COALESCE(m.TotalAll, 0),
                   COALESCE(m.SuccessAll, 0),
                   COALESCE(m.FailureAll, 0)
            FROM Devices d
            LEFT JOIN LogMetrics m ON m.DeviceId = d.Id
            {deviceFilterSql}
            ORDER BY d.Name COLLATE NOCASE, d.IpAddress;
            """;

        AddParameter(command, "@Since24", ToStorageDate(now.AddHours(-24)));
        AddParameter(command, "@Since7", ToStorageDate(now.AddDays(-7)));
        AddParameter(command, "@Since30", ToStorageDate(now.AddDays(-30)));
        if (ids is not null)
        {
            for (var index = 0; index < ids.Count; index++)
            {
                AddParameter(command, idParameterNames[index], ids[index]);
            }
        }

        await using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            items.Add(new UptimeReportItem
            {
                DeviceId = reader.GetInt32(0),
                DeviceName = reader.GetString(1),
                IpAddress = reader.GetString(2),
                DeviceType = DeviceTypeExtensions.FromStorageValue(reader.GetString(3)),
                GroupName = reader.GetString(4),
                HealthStatus = DeviceStatusExtensions.FromStorageValue(reader.GetString(5)),
                LastCheckAt = ReadNullableDate(reader, 6),
                LastSuccessfulCheckAt = ReadNullableDate(reader, 7),
                LastFailedCheckAt = ReadNullableDate(reader, 8),
                LatencyMs = reader.IsDBNull(9) ? null : reader.GetInt64(9),
                ConsecutiveFailureCount = ReadInt32(reader, 10),
                TotalChecks24h = ReadInt32(reader, 11),
                SuccessfulChecks24h = ReadInt32(reader, 12),
                FailedChecks24h = ReadInt32(reader, 13),
                TotalChecks7d = ReadInt32(reader, 14),
                SuccessfulChecks7d = ReadInt32(reader, 15),
                FailedChecks7d = ReadInt32(reader, 16),
                TotalChecks30d = ReadInt32(reader, 17),
                SuccessfulChecks30d = ReadInt32(reader, 18),
                FailedChecks30d = ReadInt32(reader, 19),
                TotalChecksOverall = ReadInt32(reader, 20),
                SuccessfulChecksOverall = ReadInt32(reader, 21),
                FailedChecksOverall = ReadInt32(reader, 22)
            });
        }

        return items;
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
            if (status.Value == DeviceStatus.Online)
            {
                where.Add($"Status IN ({SuccessStatusSql})");
            }
            else if (status.Value == DeviceStatus.Offline)
            {
                where.Add("Status IN ('Offline','Unreachable')");
            }
            else
            {
                where.Add("Status = @Status");
                AddParameter(command, "@Status", status.Value.ToStorageValue());
            }
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
            where.Add($"Status IN ({FailureStatusSql})");
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
        command.CommandText = $"""
            SELECT COUNT(1)
            FROM PingLogs
            WHERE Status IN ({FailureStatusSql})
              AND CheckedAt >= @Since;
            """;
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

    private const string SuccessStatusSql = "'Online','Reachable'";

    private const string FailureStatusSql = "'Warning','UnderWatch','Offline','Unreachable','PingBlockedOrNoReply'";

    private const string MeasuredStatusSql = SuccessStatusSql + "," + FailureStatusSql;

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

    private static double? CalculateUptime(int successCount, int totalCount)
    {
        return totalCount == 0 ? null : successCount * 100d / totalCount;
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

    private static int ReadInt32(SqliteDataReader reader, int ordinal)
    {
        return reader.IsDBNull(ordinal)
            ? 0
            : Convert.ToInt32(reader.GetValue(ordinal), CultureInfo.InvariantCulture);
    }

    private static DateTime? ReadNullableDate(SqliteDataReader reader, int ordinal)
    {
        return reader.IsDBNull(ordinal) ? null : FromStorageDate(reader.GetString(ordinal));
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
