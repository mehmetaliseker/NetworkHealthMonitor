using System.Globalization;
using Microsoft.Data.Sqlite;
using NetworkHealthMonitor.Models;

namespace NetworkHealthMonitor.Data;

public sealed class WorkerHeartbeatRepository
{
    private readonly SqliteConnectionFactory _connectionFactory;

    public WorkerHeartbeatRepository(SqliteConnectionFactory connectionFactory)
    {
        _connectionFactory = connectionFactory;
    }

    public async Task UpsertStartedAsync(WorkerHeartbeatSnapshot heartbeat, CancellationToken cancellationToken = default)
    {
        await using var connection = await _connectionFactory.CreateOpenConnectionAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO WorkerHeartbeat
                (WorkerInstanceId, MachineName, ProcessId, Version, StartedAtUtc, LastSeenAtUtc,
                 LastSchedulerCycleAtUtc, LastSuccessfulPingAtUtc, LastNotificationDispatchAtUtc, Status, LastError)
            VALUES
                (@WorkerInstanceId, @MachineName, @ProcessId, @Version, @StartedAtUtc, @LastSeenAtUtc,
                 @LastSchedulerCycleAtUtc, @LastSuccessfulPingAtUtc, @LastNotificationDispatchAtUtc, @Status, @LastError)
            ON CONFLICT(WorkerInstanceId) DO UPDATE SET
                MachineName = excluded.MachineName,
                ProcessId = excluded.ProcessId,
                Version = excluded.Version,
                StartedAtUtc = excluded.StartedAtUtc,
                LastSeenAtUtc = excluded.LastSeenAtUtc,
                Status = excluded.Status,
                LastError = excluded.LastError;
            """;
        AddHeartbeatParameters(command, heartbeat);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task TouchAsync(string workerInstanceId, string status, string lastError = "", CancellationToken cancellationToken = default)
    {
        await using var connection = await _connectionFactory.CreateOpenConnectionAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = """
            UPDATE WorkerHeartbeat
            SET LastSeenAtUtc = @LastSeenAtUtc,
                Status = @Status,
                LastError = @LastError
            WHERE WorkerInstanceId = @WorkerInstanceId;
            """;
        AddParameter(command, "@LastSeenAtUtc", ToStorageDate(DateTime.UtcNow));
        AddParameter(command, "@Status", status);
        AddParameter(command, "@LastError", lastError);
        AddParameter(command, "@WorkerInstanceId", workerInstanceId);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public Task MarkSchedulerCycleAsync(string workerInstanceId, DateTime whenUtc, CancellationToken cancellationToken = default)
    {
        return UpdateDateColumnAsync(workerInstanceId, "LastSchedulerCycleAtUtc", whenUtc, cancellationToken);
    }

    public async Task MarkSchedulerCycleAsync(
        string workerInstanceId,
        DateTime whenUtc,
        double cycleMilliseconds,
        CancellationToken cancellationToken = default)
    {
        await using var connection = await _connectionFactory.CreateOpenConnectionAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = """
            UPDATE WorkerHeartbeat
            SET LastSchedulerCycleAtUtc = @Value,
                LastSeenAtUtc = @Value,
                AverageSchedulerCycleMs = CASE
                    WHEN AverageSchedulerCycleMs <= 0 THEN @CycleMs
                    ELSE ((AverageSchedulerCycleMs * 4.0) + @CycleMs) / 5.0
                END,
                Status = 'Running'
            WHERE WorkerInstanceId = @WorkerInstanceId;
            """;
        AddParameter(command, "@Value", ToStorageDate(whenUtc));
        AddParameter(command, "@CycleMs", Math.Max(0, cycleMilliseconds));
        AddParameter(command, "@WorkerInstanceId", workerInstanceId);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public Task MarkSuccessfulPingAsync(string workerInstanceId, DateTime whenUtc, CancellationToken cancellationToken = default)
    {
        return UpdateDateColumnAsync(workerInstanceId, "LastSuccessfulPingAtUtc", whenUtc, cancellationToken);
    }

    public Task MarkNotificationDispatchAsync(string workerInstanceId, DateTime whenUtc, CancellationToken cancellationToken = default)
    {
        return UpdateDateColumnAsync(workerInstanceId, "LastNotificationDispatchAtUtc", whenUtc, cancellationToken);
    }

    public async Task MarkDiagnosticErrorAsync(
        string workerInstanceId,
        string columnName,
        string error,
        CancellationToken cancellationToken = default)
    {
        if (!AllowedDiagnosticColumns.Contains(columnName))
        {
            throw new ArgumentOutOfRangeException(nameof(columnName), "Unsupported diagnostic column.");
        }

        await using var connection = await _connectionFactory.CreateOpenConnectionAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = $"""
            UPDATE WorkerHeartbeat
            SET {columnName} = @Error,
                LastError = @Error,
                LastSeenAtUtc = @LastSeenAtUtc,
                Status = 'Warning'
            WHERE WorkerInstanceId = @WorkerInstanceId;
            """;
        AddParameter(command, "@Error", LimitError(error));
        AddParameter(command, "@LastSeenAtUtc", ToStorageDate(DateTime.UtcNow));
        AddParameter(command, "@WorkerInstanceId", workerInstanceId);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task<WorkerHeartbeatSnapshot?> GetLatestAsync(CancellationToken cancellationToken = default)
    {
        await using var connection = await _connectionFactory.CreateOpenConnectionAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT WorkerInstanceId, MachineName, ProcessId, Version, StartedAtUtc, LastSeenAtUtc,
                   LastSchedulerCycleAtUtc, LastSuccessfulPingAtUtc, LastNotificationDispatchAtUtc, Status, LastError,
                   LastCriticalError, LastDatabaseLockedError, LastSchedulerException, LastNtfyException, AverageSchedulerCycleMs
            FROM WorkerHeartbeat
            ORDER BY LastSeenAtUtc DESC
            LIMIT 1;
            """;
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken))
        {
            return null;
        }

        return new WorkerHeartbeatSnapshot
        {
            WorkerInstanceId = reader.GetString(0),
            MachineName = reader.GetString(1),
            ProcessId = reader.GetInt32(2),
            Version = reader.GetString(3),
            StartedAtUtc = FromStorageDate(reader.GetString(4)),
            LastSeenAtUtc = FromStorageDate(reader.GetString(5)),
            LastSchedulerCycleAtUtc = reader.IsDBNull(6) ? null : FromStorageDate(reader.GetString(6)),
            LastSuccessfulPingAtUtc = reader.IsDBNull(7) ? null : FromStorageDate(reader.GetString(7)),
            LastNotificationDispatchAtUtc = reader.IsDBNull(8) ? null : FromStorageDate(reader.GetString(8)),
            Status = reader.GetString(9),
            LastError = reader.GetString(10),
            LastCriticalError = reader.GetString(11),
            LastDatabaseLockedError = reader.GetString(12),
            LastSchedulerException = reader.GetString(13),
            LastNtfyException = reader.GetString(14),
            AverageSchedulerCycleMs = reader.GetDouble(15)
        };
    }

    private async Task UpdateDateColumnAsync(string workerInstanceId, string columnName, DateTime whenUtc, CancellationToken cancellationToken)
    {
        await using var connection = await _connectionFactory.CreateOpenConnectionAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = $"""
            UPDATE WorkerHeartbeat
            SET {columnName} = @Value,
                LastSeenAtUtc = @Value,
                Status = 'Running'
            WHERE WorkerInstanceId = @WorkerInstanceId;
            """;
        AddParameter(command, "@Value", ToStorageDate(whenUtc));
        AddParameter(command, "@WorkerInstanceId", workerInstanceId);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static void AddHeartbeatParameters(SqliteCommand command, WorkerHeartbeatSnapshot heartbeat)
    {
        AddParameter(command, "@WorkerInstanceId", heartbeat.WorkerInstanceId);
        AddParameter(command, "@MachineName", heartbeat.MachineName);
        AddParameter(command, "@ProcessId", heartbeat.ProcessId);
        AddParameter(command, "@Version", heartbeat.Version);
        AddParameter(command, "@StartedAtUtc", ToStorageDate(heartbeat.StartedAtUtc));
        AddParameter(command, "@LastSeenAtUtc", ToStorageDate(heartbeat.LastSeenAtUtc));
        AddParameter(command, "@LastSchedulerCycleAtUtc", heartbeat.LastSchedulerCycleAtUtc.HasValue ? ToStorageDate(heartbeat.LastSchedulerCycleAtUtc.Value) : null);
        AddParameter(command, "@LastSuccessfulPingAtUtc", heartbeat.LastSuccessfulPingAtUtc.HasValue ? ToStorageDate(heartbeat.LastSuccessfulPingAtUtc.Value) : null);
        AddParameter(command, "@LastNotificationDispatchAtUtc", heartbeat.LastNotificationDispatchAtUtc.HasValue ? ToStorageDate(heartbeat.LastNotificationDispatchAtUtc.Value) : null);
        AddParameter(command, "@Status", heartbeat.Status);
        AddParameter(command, "@LastError", heartbeat.LastError);
    }

    private static readonly HashSet<string> AllowedDiagnosticColumns = new(StringComparer.Ordinal)
    {
        "LastCriticalError",
        "LastDatabaseLockedError",
        "LastSchedulerException",
        "LastNtfyException"
    };

    private static void AddParameter(SqliteCommand command, string name, object? value)
    {
        command.Parameters.AddWithValue(name, value ?? DBNull.Value);
    }

    private static string ToStorageDate(DateTime value)
    {
        return value.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture);
    }

    private static string LimitError(string value)
    {
        var safe = value.ReplaceLineEndings(" ").Trim();
        return safe.Length <= 500 ? safe : safe[..500];
    }

    private static DateTime FromStorageDate(string value)
    {
        return DateTime.TryParse(
            value,
            CultureInfo.InvariantCulture,
            DateTimeStyles.RoundtripKind,
            out var parsed)
            ? parsed.ToUniversalTime()
            : DateTime.UtcNow;
    }
}
