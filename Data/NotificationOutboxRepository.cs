using System.Globalization;
using Microsoft.Data.Sqlite;
using NetworkHealthMonitor.Models;
using NetworkHealthMonitor.Services;

namespace NetworkHealthMonitor.Data;

public sealed class NotificationOutboxRepository : INotificationOutboxRepository
{
    private readonly SqliteConnectionFactory _connectionFactory;

    public NotificationOutboxRepository(SqliteConnectionFactory connectionFactory)
    {
        _connectionFactory = connectionFactory;
    }

    public async Task<long> AddPendingAsync(
        string eventType,
        int? deviceId,
        long? incidentId,
        string payloadJson,
        string deduplicationKey,
        DateTime nowUtc,
        CancellationToken cancellationToken = default)
    {
        await using var connection = await _connectionFactory.CreateOpenConnectionAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = InsertSql + " SELECT last_insert_rowid();";
        AddInsertParameters(command, eventType, deviceId, incidentId, payloadJson, deduplicationKey, nowUtc);
        var result = await command.ExecuteScalarAsync(cancellationToken);
        return Convert.ToInt64(result, CultureInfo.InvariantCulture);
    }

    public async Task<long> AddPendingAsync(
        NotificationOutboxCreateRequest request,
        DateTime nowUtc,
        CancellationToken cancellationToken = default)
    {
        await using var connection = await _connectionFactory.CreateOpenConnectionAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = InsertSql + " SELECT last_insert_rowid();";
        AddInsertParameters(command, request, nowUtc);
        var result = await command.ExecuteScalarAsync(cancellationToken);
        return Convert.ToInt64(result, CultureInfo.InvariantCulture);
    }

    public static async Task<long> AddPendingAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        string eventType,
        int? deviceId,
        long? incidentId,
        string payloadJson,
        string deduplicationKey,
        DateTime nowUtc,
        CancellationToken cancellationToken = default)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = InsertSql + " SELECT last_insert_rowid();";
        AddInsertParameters(command, eventType, deviceId, incidentId, payloadJson, deduplicationKey, nowUtc);
        var result = await command.ExecuteScalarAsync(cancellationToken);
        return Convert.ToInt64(result, CultureInfo.InvariantCulture);
    }

    public static async Task<long> AddPendingAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        NotificationOutboxCreateRequest request,
        DateTime nowUtc,
        CancellationToken cancellationToken = default)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = InsertSql + " SELECT last_insert_rowid();";
        AddInsertParameters(command, request, nowUtc);
        var result = await command.ExecuteScalarAsync(cancellationToken);
        return Convert.ToInt64(result, CultureInfo.InvariantCulture);
    }

    public async Task<IReadOnlyList<NotificationOutboxItem>> ClaimDueAsync(
        int maxItems,
        string lockOwner,
        DateTime nowUtc,
        TimeSpan processingTimeout,
        CancellationToken cancellationToken = default)
    {
        var claimed = new List<NotificationOutboxItem>();
        await using var connection = await _connectionFactory.CreateOpenConnectionAsync();
        using var transaction = connection.BeginTransaction();

        await using (var reset = connection.CreateCommand())
        {
            reset.Transaction = transaction;
            reset.CommandText = """
                UPDATE NotificationOutbox
                SET Status = 'Pending',
                    LockedAtUtc = NULL,
                    LockedBy = ''
                WHERE Status = 'Processing'
                  AND LockedAtUtc IS NOT NULL
                  AND LockedAtUtc < @StaleBeforeUtc;
                """;
            AddParameter(reset, "@StaleBeforeUtc", ToStorageDate(nowUtc.Subtract(processingTimeout)));
            await reset.ExecuteNonQueryAsync(cancellationToken);
        }

        await using var select = connection.CreateCommand();
        select.Transaction = transaction;
        select.CommandText = """
            SELECT Id, EventType, DeviceId, '' AS DeviceName, IncidentId, Channel, Recipient, Subject, Body,
                   PayloadJson, DeduplicationKey, IdempotencyKey, Status,
                   AttemptCount, NextAttemptAtUtc, LockedAtUtc, LastAttemptAtUtc, LockedBy, LastError,
                   CreatedAtUtc, SentAtUtc, CancelledAtUtc
            FROM NotificationOutbox
            WHERE Status = 'Pending'
              AND NextAttemptAtUtc <= @NowUtc
            ORDER BY NextAttemptAtUtc, Id
            LIMIT @Limit;
            """;
        AddParameter(select, "@NowUtc", ToStorageDate(nowUtc));
        AddParameter(select, "@Limit", Math.Clamp(maxItems, 1, 100));

        var ids = new List<long>();
        await using (var reader = await select.ExecuteReaderAsync(cancellationToken))
        {
            while (await reader.ReadAsync(cancellationToken))
            {
                var item = ReadItem(reader);
                claimed.Add(item);
                ids.Add(item.Id);
            }
        }

        foreach (var id in ids)
        {
            await using var update = connection.CreateCommand();
            update.Transaction = transaction;
            update.CommandText = """
                UPDATE NotificationOutbox
                SET Status = 'Processing',
                    LockedAtUtc = @LockedAtUtc,
                    LockedBy = @LockedBy
                WHERE Id = @Id
                  AND Status = 'Pending';
                """;
            AddParameter(update, "@LockedAtUtc", ToStorageDate(nowUtc));
            AddParameter(update, "@LockedBy", lockOwner);
            AddParameter(update, "@Id", id);
            await update.ExecuteNonQueryAsync(cancellationToken);
        }

        transaction.Commit();
        return claimed;
    }

    public async Task MarkSentAsync(long id, DateTime sentAtUtc, CancellationToken cancellationToken = default)
    {
        await ExecuteStatusUpdateAsync(
            id,
            """
            UPDATE NotificationOutbox
            SET Status = 'Sent',
                SentAtUtc = @NowUtc,
                LastAttemptAtUtc = @NowUtc,
                LockedAtUtc = NULL,
                LockedBy = '',
                LastError = ''
            WHERE Id = @Id;
            """,
            command => AddParameter(command, "@NowUtc", ToStorageDate(sentAtUtc)),
            cancellationToken);
    }

    public async Task MarkRetryAsync(long id, int attemptCount, DateTime nextAttemptAtUtc, string safeError, CancellationToken cancellationToken = default)
    {
        await ExecuteStatusUpdateAsync(
            id,
            """
            UPDATE NotificationOutbox
            SET Status = 'Pending',
                AttemptCount = @AttemptCount,
                NextAttemptAtUtc = @NextAttemptAtUtc,
                LastAttemptAtUtc = @LastAttemptAtUtc,
                LockedAtUtc = NULL,
                LockedBy = '',
                LastError = @LastError
            WHERE Id = @Id;
            """,
            command =>
            {
                AddParameter(command, "@AttemptCount", attemptCount);
                AddParameter(command, "@NextAttemptAtUtc", ToStorageDate(nextAttemptAtUtc));
                AddParameter(command, "@LastAttemptAtUtc", ToStorageDate(DateTime.UtcNow));
                AddParameter(command, "@LastError", LimitError(safeError));
            },
            cancellationToken);
    }

    public async Task MarkFailedAsync(long id, int attemptCount, string safeError, CancellationToken cancellationToken = default)
    {
        await ExecuteStatusUpdateAsync(
            id,
            """
            UPDATE NotificationOutbox
            SET Status = 'Failed',
                AttemptCount = @AttemptCount,
                LastAttemptAtUtc = @LastAttemptAtUtc,
                LockedAtUtc = NULL,
                LockedBy = '',
                LastError = @LastError
            WHERE Id = @Id;
            """,
            command =>
            {
                AddParameter(command, "@AttemptCount", attemptCount);
                AddParameter(command, "@LastAttemptAtUtc", ToStorageDate(DateTime.UtcNow));
                AddParameter(command, "@LastError", LimitError(safeError));
            },
            cancellationToken);
    }

    public async Task MarkDeadLetterAsync(long id, int attemptCount, string safeError, CancellationToken cancellationToken = default)
    {
        await ExecuteStatusUpdateAsync(
            id,
            """
            UPDATE NotificationOutbox
            SET Status = 'DeadLetter',
                AttemptCount = @AttemptCount,
                LastAttemptAtUtc = @LastAttemptAtUtc,
                LockedAtUtc = NULL,
                LockedBy = '',
                LastError = @LastError
            WHERE Id = @Id;
            """,
            command =>
            {
                AddParameter(command, "@AttemptCount", attemptCount);
                AddParameter(command, "@LastAttemptAtUtc", ToStorageDate(DateTime.UtcNow));
                AddParameter(command, "@LastError", LimitError(safeError));
            },
            cancellationToken);
    }

    public async Task<int> CancelPendingForDeviceAsync(int deviceId, DateTime nowUtc, CancellationToken cancellationToken = default)
    {
        await using var connection = await _connectionFactory.CreateOpenConnectionAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = """
            UPDATE NotificationOutbox
            SET Status = 'Cancelled',
                CancelledAtUtc = @CancelledAtUtc,
                LockedAtUtc = NULL,
                LockedBy = '',
                LastError = 'Device was deleted before notification dispatch.'
            WHERE DeviceId = @DeviceId
              AND Status IN ('Pending','Processing','Failed','DeadLetter')
              AND EventType IN ('DeviceDown','DeviceSuspectedOffline','DeviceOfflineEscalated','DeviceRecovered','Test');
            """;
        AddParameter(command, "@CancelledAtUtc", ToStorageDate(nowUtc));
        AddParameter(command, "@DeviceId", deviceId);
        return await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task<(int Pending, int Failed)> GetCountsAsync(CancellationToken cancellationToken = default)
    {
        await using var connection = await _connectionFactory.CreateOpenConnectionAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT
                SUM(CASE WHEN Status IN ('Pending','Processing') THEN 1 ELSE 0 END) AS PendingCount,
                SUM(CASE WHEN Status IN ('Failed','DeadLetter') THEN 1 ELSE 0 END) AS FailedCount
            FROM NotificationOutbox;
            """;
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken))
        {
            return (0, 0);
        }

        return (ReadInt32(reader, 0), ReadInt32(reader, 1));
    }

    public async Task<IReadOnlyList<NotificationOutboxItem>> GetFilteredAsync(
        string? status,
        string? eventType,
        int? deviceId,
        DateTime? startUtc,
        DateTime? endUtc,
        int limit,
        CancellationToken cancellationToken = default)
    {
        var items = new List<NotificationOutboxItem>();
        await using var connection = await _connectionFactory.CreateOpenConnectionAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT n.Id, n.EventType, n.DeviceId, COALESCE(d.Name, '') AS DeviceName, n.IncidentId,
                   n.Channel, n.Recipient, n.Subject, n.Body,
                   n.PayloadJson, n.DeduplicationKey, n.IdempotencyKey, n.Status, n.AttemptCount, n.NextAttemptAtUtc,
                   n.LockedAtUtc, n.LastAttemptAtUtc, n.LockedBy, n.LastError, n.CreatedAtUtc,
                   n.SentAtUtc, n.CancelledAtUtc
            FROM NotificationOutbox n
            LEFT JOIN Devices d ON d.Id = n.DeviceId
            WHERE (@Status = '' OR n.Status = @Status)
              AND (@EventType = '' OR n.EventType = @EventType)
              AND (@DeviceId IS NULL OR n.DeviceId = @DeviceId)
              AND (@StartUtc IS NULL OR n.CreatedAtUtc >= @StartUtc)
              AND (@EndUtc IS NULL OR n.CreatedAtUtc <= @EndUtc)
            ORDER BY n.CreatedAtUtc DESC
            LIMIT @Limit;
            """;
        AddParameter(command, "@Status", status?.Trim() ?? string.Empty);
        AddParameter(command, "@EventType", eventType?.Trim() ?? string.Empty);
        AddParameter(command, "@DeviceId", deviceId);
        AddParameter(command, "@StartUtc", startUtc.HasValue ? ToStorageDate(startUtc.Value) : null);
        AddParameter(command, "@EndUtc", endUtc.HasValue ? ToStorageDate(endUtc.Value) : null);
        AddParameter(command, "@Limit", Math.Clamp(limit, 1, 5000));

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            items.Add(ReadItem(reader));
        }

        return items;
    }

    public async Task<int> RetryFailedAsync(
        IEnumerable<long> ids,
        DateTime nowUtc,
        bool resetAttemptCount = true,
        CancellationToken cancellationToken = default)
    {
        var materialized = ids.Where(id => id > 0).Distinct().ToList();
        if (materialized.Count == 0)
        {
            return 0;
        }

        var affected = 0;
        await using var connection = await _connectionFactory.CreateOpenConnectionAsync();
        using var transaction = connection.BeginTransaction();
        foreach (var id in materialized)
        {
            await using var command = connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText = """
                UPDATE NotificationOutbox
                SET Status = 'Pending',
                    AttemptCount = CASE WHEN @ResetAttemptCount = 1 THEN 0 ELSE AttemptCount END,
                    NextAttemptAtUtc = @NextAttemptAtUtc,
                    LockedAtUtc = NULL,
                    LockedBy = '',
                    LastError = ''
                WHERE Id = @Id
                  AND Status IN ('Failed','DeadLetter');
                """;
            AddParameter(command, "@ResetAttemptCount", resetAttemptCount ? 1 : 0);
            AddParameter(command, "@NextAttemptAtUtc", ToStorageDate(nowUtc));
            AddParameter(command, "@Id", id);
            affected += await command.ExecuteNonQueryAsync(cancellationToken);
        }

        transaction.Commit();
        return affected;
    }

    public async Task<int> CancelPendingAsync(IEnumerable<long> ids, DateTime nowUtc, CancellationToken cancellationToken = default)
    {
        var materialized = ids.Where(id => id > 0).Distinct().ToList();
        if (materialized.Count == 0)
        {
            return 0;
        }

        var affected = 0;
        await using var connection = await _connectionFactory.CreateOpenConnectionAsync();
        using var transaction = connection.BeginTransaction();
        foreach (var id in materialized)
        {
            await using var command = connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText = """
                UPDATE NotificationOutbox
                SET Status = 'Cancelled',
                    CancelledAtUtc = @CancelledAtUtc,
                    LockedAtUtc = NULL,
                    LockedBy = '',
                    LastError = 'Cancelled by UI.'
                WHERE Id = @Id
                  AND Status = 'Pending';
                """;
            AddParameter(command, "@CancelledAtUtc", ToStorageDate(nowUtc));
            AddParameter(command, "@Id", id);
            affected += await command.ExecuteNonQueryAsync(cancellationToken);
        }

        transaction.Commit();
        return affected;
    }

    private async Task ExecuteStatusUpdateAsync(
        long id,
        string commandText,
        Action<SqliteCommand> addParameters,
        CancellationToken cancellationToken)
    {
        await using var connection = await _connectionFactory.CreateOpenConnectionAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = commandText;
        addParameters(command);
        AddParameter(command, "@Id", id);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private const string InsertSql = """
        INSERT OR IGNORE INTO NotificationOutbox
            (EventType, DeviceId, IncidentId, Channel, Recipient, Subject, Body, PayloadJson,
             DeduplicationKey, IdempotencyKey, Status, AttemptCount,
             NextAttemptAtUtc, LockedAtUtc, LastAttemptAtUtc, LockedBy, LastError, CreatedAtUtc, SentAtUtc, CancelledAtUtc)
        VALUES
            (@EventType, @DeviceId, @IncidentId, @Channel, @Recipient, @Subject, @Body, @PayloadJson,
             @DeduplicationKey, @IdempotencyKey, 'Pending', 0,
             @NextAttemptAtUtc, NULL, NULL, '', '', @CreatedAtUtc, NULL, NULL);
        """;

    private static void AddInsertParameters(
        SqliteCommand command,
        string eventType,
        int? deviceId,
        long? incidentId,
        string payloadJson,
        string deduplicationKey,
        DateTime nowUtc)
    {
        AddInsertParameters(
            command,
            new NotificationOutboxCreateRequest
            {
                EventType = eventType,
                DeviceId = deviceId,
                IncidentId = incidentId,
                Channel = NotificationChannels.Ntfy,
                PayloadJson = payloadJson,
                IdempotencyKey = deduplicationKey
            },
            nowUtc);
    }

    private static void AddInsertParameters(
        SqliteCommand command,
        NotificationOutboxCreateRequest request,
        DateTime nowUtc)
    {
        var idempotencyKey = string.IsNullOrWhiteSpace(request.IdempotencyKey)
            ? CreateFallbackIdempotencyKey(request)
            : request.IdempotencyKey.Trim();
        AddParameter(command, "@EventType", request.EventType.Trim());
        AddParameter(command, "@DeviceId", request.DeviceId);
        AddParameter(command, "@IncidentId", request.IncidentId);
        AddParameter(command, "@Channel", string.IsNullOrWhiteSpace(request.Channel) ? NotificationChannels.Ntfy : request.Channel.Trim());
        AddParameter(command, "@Recipient", request.Recipient.Trim());
        AddParameter(command, "@Subject", request.Subject.Trim());
        AddParameter(command, "@Body", request.Body);
        AddParameter(command, "@PayloadJson", request.PayloadJson);
        AddParameter(command, "@DeduplicationKey", idempotencyKey);
        AddParameter(command, "@IdempotencyKey", idempotencyKey);
        AddParameter(command, "@NextAttemptAtUtc", ToStorageDate(nowUtc));
        AddParameter(command, "@CreatedAtUtc", ToStorageDate(nowUtc));
    }

    private static NotificationOutboxItem ReadItem(SqliteDataReader reader)
    {
        return new NotificationOutboxItem
        {
            Id = reader.GetInt64(0),
            EventType = reader.GetString(1),
            DeviceId = reader.IsDBNull(2) ? null : reader.GetInt32(2),
            DeviceName = reader.GetString(3),
            IncidentId = reader.IsDBNull(4) ? null : reader.GetInt64(4),
            Channel = reader.GetString(5),
            Recipient = reader.GetString(6),
            Subject = reader.GetString(7),
            Body = reader.GetString(8),
            PayloadJson = reader.GetString(9),
            DeduplicationKey = reader.GetString(10),
            IdempotencyKey = reader.GetString(11),
            Status = reader.GetString(12),
            AttemptCount = reader.GetInt32(13),
            NextAttemptAtUtc = FromStorageDate(reader.GetString(14)),
            LockedAtUtc = reader.IsDBNull(15) ? null : FromStorageDate(reader.GetString(15)),
            LastAttemptAtUtc = reader.IsDBNull(16) ? null : FromStorageDate(reader.GetString(16)),
            LockedBy = reader.GetString(17),
            LastError = reader.GetString(18),
            CreatedAtUtc = FromStorageDate(reader.GetString(19)),
            SentAtUtc = reader.IsDBNull(20) ? null : FromStorageDate(reader.GetString(20)),
            CancelledAtUtc = reader.IsDBNull(21) ? null : FromStorageDate(reader.GetString(21))
        };
    }

    private static string CreateFallbackIdempotencyKey(NotificationOutboxCreateRequest request)
    {
        return $"{request.EventType}:{request.Channel}:{request.DeviceId}:{request.IncidentId}:{request.Recipient}"
            .Trim()
            .ToLowerInvariant();
    }

    private static int ReadInt32(SqliteDataReader reader, int ordinal)
    {
        return reader.IsDBNull(ordinal)
            ? 0
            : Convert.ToInt32(reader.GetValue(ordinal), CultureInfo.InvariantCulture);
    }

    private static string LimitError(string value)
    {
        var safe = value.ReplaceLineEndings(" ").Trim();
        return safe.Length <= 500 ? safe : safe[..500];
    }

    private static void AddParameter(SqliteCommand command, string name, object? value)
    {
        command.Parameters.AddWithValue(name, value ?? DBNull.Value);
    }

    private static string ToStorageDate(DateTime value)
    {
        return value.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture);
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
