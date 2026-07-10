using System.Globalization;
using Microsoft.Data.Sqlite;
using NetworkHealthMonitor.Models;

namespace NetworkHealthMonitor.Data;

public sealed class OutageRepository
{
    private readonly SqliteConnectionFactory _connectionFactory;

    public OutageRepository(SqliteConnectionFactory connectionFactory)
    {
        _connectionFactory = connectionFactory;
    }

    public async Task<IReadOnlyList<Outage>> GetOpenAsync()
    {
        return await GetFilteredAsync(onlyOpen: true, limit: 500);
    }

    public async Task<IReadOnlyList<Outage>> GetRecentAsync(int limit = 1000)
    {
        return await GetFilteredAsync(onlyOpen: false, limit: limit);
    }

    public async Task SyncFromPingResultsAsync(
        IReadOnlyList<PingDeviceResult> results,
        IReadOnlyDictionary<int, int?> recoveryLogIds)
    {
        if (results.Count == 0)
        {
            return;
        }

        await using var connection = await _connectionFactory.CreateOpenConnectionAsync();
        using var transaction = connection.BeginTransaction();

        foreach (var result in results)
        {
            if (result.IsSuccess)
            {
                await using var resolve = connection.CreateCommand();
                resolve.Transaction = transaction;
                resolve.CommandText = """
                    UPDATE Outages
                    SET EndedAt = @EndedAt,
                        RecoveryPingLogId = @RecoveryPingLogId,
                        IsResolved = 1
                    WHERE DeviceId = @DeviceId
                      AND IsResolved = 0;
                    """;
                AddParameter(resolve, "@EndedAt", ToStorageDate(result.CheckedAt));
                AddParameter(resolve, "@RecoveryPingLogId", recoveryLogIds.TryGetValue(result.Device.Id, out var logId) ? logId : null);
                AddParameter(resolve, "@DeviceId", result.Device.Id);
                await resolve.ExecuteNonQueryAsync();
                continue;
            }

            if (result.Status != DeviceStatus.Offline)
            {
                continue;
            }

            var failureCount = result.Device.ConsecutiveFailures + 1;
            await using var select = connection.CreateCommand();
            select.Transaction = transaction;
            select.CommandText = """
                SELECT Id
                FROM Outages
                WHERE DeviceId = @DeviceId
                  AND IsResolved = 0
                ORDER BY StartedAt DESC
                LIMIT 1;
                """;
            AddParameter(select, "@DeviceId", result.Device.Id);
            var existingId = await select.ExecuteScalarAsync();

            if (existingId is null || existingId == DBNull.Value)
            {
                await using var insert = connection.CreateCommand();
                insert.Transaction = transaction;
                insert.CommandText = """
                    INSERT INTO Outages (DeviceId, StartedAt, EndedAt, FailureCount, RecoveryPingLogId, IsResolved, CreatedAt)
                    VALUES (@DeviceId, @StartedAt, NULL, @FailureCount, NULL, 0, @CreatedAt);
                    """;
                AddParameter(insert, "@DeviceId", result.Device.Id);
                AddParameter(insert, "@StartedAt", ToStorageDate(result.CheckedAt));
                AddParameter(insert, "@FailureCount", failureCount);
                AddParameter(insert, "@CreatedAt", ToStorageDate(DateTime.Now));
                await insert.ExecuteNonQueryAsync();
            }
            else
            {
                await using var update = connection.CreateCommand();
                update.Transaction = transaction;
                update.CommandText = "UPDATE Outages SET FailureCount = @FailureCount WHERE Id = @Id;";
                AddParameter(update, "@FailureCount", failureCount);
                AddParameter(update, "@Id", Convert.ToInt32(existingId, CultureInfo.InvariantCulture));
                await update.ExecuteNonQueryAsync();
            }
        }

        transaction.Commit();
    }

    public async Task<IReadOnlyDictionary<int, IReadOnlyList<Outage>>> GetByDeviceSinceAsync(DateTime since)
    {
        var outages = new List<Outage>();
        await using var connection = await _connectionFactory.CreateOpenConnectionAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT o.Id, o.DeviceId, d.Name, d.IpAddress, d.DeviceType, d.GroupName, o.StartedAt,
                   o.EndedAt, o.FailureCount, o.RecoveryPingLogId, o.IsResolved
            FROM Outages o
            INNER JOIN Devices d ON d.Id = o.DeviceId
            WHERE o.StartedAt >= @Since
               OR o.EndedAt >= @Since
               OR o.IsResolved = 0
            ORDER BY o.StartedAt DESC;
            """;
        AddParameter(command, "@Since", ToStorageDate(since));

        await using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            outages.Add(ReadOutage(reader));
        }

        return outages
            .GroupBy(outage => outage.DeviceId)
            .ToDictionary(group => group.Key, group => (IReadOnlyList<Outage>)group.ToList());
    }

    private async Task<IReadOnlyList<Outage>> GetFilteredAsync(bool onlyOpen, int limit)
    {
        var outages = new List<Outage>();
        await using var connection = await _connectionFactory.CreateOpenConnectionAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = $"""
            SELECT o.Id, o.DeviceId, d.Name, d.IpAddress, d.DeviceType, d.GroupName, o.StartedAt,
                   o.EndedAt, o.FailureCount, o.RecoveryPingLogId, o.IsResolved
            FROM Outages o
            INNER JOIN Devices d ON d.Id = o.DeviceId
            {(onlyOpen ? "WHERE o.IsResolved = 0" : string.Empty)}
            ORDER BY o.IsResolved ASC, o.StartedAt DESC
            LIMIT @Limit;
            """;
        AddParameter(command, "@Limit", Math.Clamp(limit, 1, 5000));

        await using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            outages.Add(ReadOutage(reader));
        }

        return outages;
    }

    private static Outage ReadOutage(SqliteDataReader reader)
    {
        return new Outage
        {
            Id = reader.GetInt32(0),
            DeviceId = reader.GetInt32(1),
            DeviceName = reader.GetString(2),
            IpAddress = reader.GetString(3),
            DeviceType = DeviceTypeExtensions.FromStorageValue(reader.GetString(4)),
            GroupName = reader.GetString(5),
            StartedAt = FromStorageDate(reader.GetString(6)),
            EndedAt = reader.IsDBNull(7) ? null : FromStorageDate(reader.GetString(7)),
            FailureCount = reader.GetInt32(8),
            RecoveryPingLogId = reader.IsDBNull(9) ? null : reader.GetInt32(9),
            IsResolved = reader.GetInt32(10) == 1
        };
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
