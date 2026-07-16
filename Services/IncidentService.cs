using System.Globalization;
using System.Text.Json;
using Microsoft.Data.Sqlite;
using NetworkHealthMonitor.Data;
using NetworkHealthMonitor.Infrastructure;
using NetworkHealthMonitor.Models;

namespace NetworkHealthMonitor.Services;

public sealed class IncidentService : IIncidentService
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private readonly SqliteConnectionFactory _connectionFactory;
    private readonly AppSettingsService _settingsService;
    private readonly IAlertPolicyService _alertPolicyService;

    public IncidentService(
        SqliteConnectionFactory connectionFactory,
        AppSettingsService settingsService,
        IAlertPolicyService alertPolicyService)
    {
        _connectionFactory = connectionFactory;
        _settingsService = settingsService;
        _alertPolicyService = alertPolicyService;
    }

    public async Task ApplyPingResultsAsync(
        IReadOnlyList<PingDeviceResult> results,
        IReadOnlyDictionary<int, PingLog> logsByDeviceId,
        CancellationToken cancellationToken = default)
    {
        if (results.Count == 0)
        {
            return;
        }

        var settings = (await _settingsService.LoadAsync()).Notifications;
        var nowUtc = DateTime.UtcNow;

        await using var connection = await _connectionFactory.CreateOpenConnectionAsync();
        using var transaction = connection.BeginTransaction();

        foreach (var result in results.OrderBy(item => item.CheckedAt))
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!logsByDeviceId.TryGetValue(result.Device.Id, out var log))
            {
                continue;
            }

            if (result.IsSuccess)
            {
                await ApplySuccessAsync(connection, transaction, result, log, settings, nowUtc, cancellationToken);
            }
            else
            {
                await ApplyFailureAsync(connection, transaction, result, log, settings, nowUtc, cancellationToken);
            }
        }

        transaction.Commit();
    }

    private async Task ApplyFailureAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        PingDeviceResult result,
        PingLog log,
        NotificationSettings settings,
        DateTime nowUtc,
        CancellationToken cancellationToken)
    {
        var failureCount = result.Device.ConsecutiveFailures + 1;
        var existing = await GetOpenIncidentAsync(connection, transaction, result.Device.Id, cancellationToken);
        if (existing.HasValue)
        {
            await using var update = connection.CreateCommand();
            update.Transaction = transaction;
            update.CommandText = """
                UPDATE DeviceIncidents
                SET CurrentFailureCount = @CurrentFailureCount,
                    RecoverySuccessCount = 0,
                    LastFailureAtUtc = @LastFailureAtUtc,
                    UpdatedAtUtc = @UpdatedAtUtc
                WHERE Id = @Id;
                """;
            AddParameter(update, "@CurrentFailureCount", Math.Max(failureCount, existing.Value.CurrentFailureCount + 1));
            AddParameter(update, "@LastFailureAtUtc", ToStorageDate(log.CheckedAt.ToUniversalTime()));
            AddParameter(update, "@UpdatedAtUtc", ToStorageDate(nowUtc));
            AddParameter(update, "@Id", existing.Value.Id);
            await update.ExecuteNonQueryAsync(cancellationToken);
            return;
        }

        if (failureCount < settings.DownFailureThreshold)
        {
            return;
        }

        var lastDownNotificationAt = await GetLastNotificationUtcAsync(connection, transaction, result.Device.Id, "DeviceDown", cancellationToken);
        var allowNotification = settings.NotifyOnDeviceDown
            && _alertPolicyService.IsNotificationAllowed(lastDownNotificationAt, nowUtc, settings.NotificationCooldownMinutes);

        var confirmedDownAtUtc = log.CheckedAt.ToUniversalTime();
        var firstFailureAtUtc = await GetFirstFailureForCurrentStreakAsync(
            connection,
            transaction,
            result.Device.Id,
            failureCount,
            confirmedDownAtUtc,
            cancellationToken) ?? confirmedDownAtUtc;

        var incidentId = await InsertIncidentAsync(
            connection,
            transaction,
            result.Device.Id,
            firstFailureAtUtc,
            confirmedDownAtUtc,
            failureCount,
            allowNotification ? nowUtc : null,
            nowUtc,
            cancellationToken);

        if (!allowNotification)
        {
            AppErrorLogger.LogInfo($"Flapping/cooldown suppressed DeviceDown notification. DeviceId={result.Device.Id} IncidentId={incidentId}");
            return;
        }

        var payload = CreateDownPayload(result, log, settings, incidentId);
        await NotificationOutboxRepository.AddPendingAsync(
            connection,
            transaction,
            "DeviceDown",
            result.Device.Id,
            incidentId,
            JsonSerializer.Serialize(payload, JsonOptions),
            CreateDeduplicationKey("DeviceDown", result.Device.Id, incidentId),
            nowUtc,
            cancellationToken);
    }

    private async Task ApplySuccessAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        PingDeviceResult result,
        PingLog log,
        NotificationSettings settings,
        DateTime nowUtc,
        CancellationToken cancellationToken)
    {
        var existing = await GetOpenIncidentAsync(connection, transaction, result.Device.Id, cancellationToken);
        if (!existing.HasValue)
        {
            return;
        }

        var recoverySuccessCount = existing.Value.RecoverySuccessCount + 1;
        if (recoverySuccessCount < settings.RecoverySuccessThreshold)
        {
            await using var updateProgress = connection.CreateCommand();
            updateProgress.Transaction = transaction;
            updateProgress.CommandText = """
                UPDATE DeviceIncidents
                SET RecoverySuccessCount = @RecoverySuccessCount,
                    LastSuccessAtUtc = @LastSuccessAtUtc,
                    UpdatedAtUtc = @UpdatedAtUtc
                WHERE Id = @Id;
                """;
            AddParameter(updateProgress, "@RecoverySuccessCount", recoverySuccessCount);
            AddParameter(updateProgress, "@LastSuccessAtUtc", ToStorageDate(log.CheckedAt.ToUniversalTime()));
            AddParameter(updateProgress, "@UpdatedAtUtc", ToStorageDate(nowUtc));
            AddParameter(updateProgress, "@Id", existing.Value.Id);
            await updateProgress.ExecuteNonQueryAsync(cancellationToken);
            return;
        }

        var lastRecoveryNotificationAt = await GetLastNotificationUtcAsync(connection, transaction, result.Device.Id, "DeviceRecovered", cancellationToken);
        var allowNotification = settings.NotifyOnDeviceRecovered
            && _alertPolicyService.IsNotificationAllowed(lastRecoveryNotificationAt, nowUtc, settings.NotificationCooldownMinutes);

        await using var close = connection.CreateCommand();
        close.Transaction = transaction;
        close.CommandText = """
            UPDATE DeviceIncidents
            SET Status = 'Closed',
                EndedAtUtc = @EndedAtUtc,
                RecoverySuccessCount = @RecoverySuccessCount,
                LastSuccessAtUtc = @LastSuccessAtUtc,
                RecoveryNotificationCreatedAtUtc = @RecoveryNotificationCreatedAtUtc,
                UpdatedAtUtc = @UpdatedAtUtc
            WHERE Id = @Id;
            """;
        AddParameter(close, "@EndedAtUtc", ToStorageDate(log.CheckedAt.ToUniversalTime()));
        AddParameter(close, "@RecoverySuccessCount", recoverySuccessCount);
        AddParameter(close, "@LastSuccessAtUtc", ToStorageDate(log.CheckedAt.ToUniversalTime()));
        AddParameter(close, "@RecoveryNotificationCreatedAtUtc", allowNotification ? ToStorageDate(nowUtc) : null);
        AddParameter(close, "@UpdatedAtUtc", ToStorageDate(nowUtc));
        AddParameter(close, "@Id", existing.Value.Id);
        await close.ExecuteNonQueryAsync(cancellationToken);

        if (!allowNotification)
        {
            return;
        }

        var payload = CreateRecoveryPayload(result, log, settings, existing.Value.Id, existing.Value.StartedAtUtc);
        await NotificationOutboxRepository.AddPendingAsync(
            connection,
            transaction,
            "DeviceRecovered",
            result.Device.Id,
            existing.Value.Id,
            JsonSerializer.Serialize(payload, JsonOptions),
            CreateDeduplicationKey("DeviceRecovered", result.Device.Id, existing.Value.Id),
            nowUtc,
            cancellationToken);
    }

    private static async Task<long> InsertIncidentAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        int deviceId,
        DateTime firstFailureAtUtc,
        DateTime confirmedDownAtUtc,
        int failureCount,
        DateTime? downNotificationCreatedAtUtc,
        DateTime nowUtc,
        CancellationToken cancellationToken)
    {
        await using var insert = connection.CreateCommand();
        insert.Transaction = transaction;
        insert.CommandText = """
            INSERT INTO DeviceIncidents
                (DeviceId, StartedAtUtc, EndedAtUtc, Status, InitialFailureCount,
                 CurrentFailureCount, RecoverySuccessCount, FirstFailureAtUtc, ConfirmedDownAtUtc,
                 DetectionDelaySeconds, LastFailureAtUtc, LastSuccessAtUtc,
                 DownNotificationCreatedAtUtc, RecoveryNotificationCreatedAtUtc, FlapCount,
                 CreatedAtUtc, UpdatedAtUtc)
            VALUES
                (@DeviceId, @StartedAtUtc, NULL, 'Open', @InitialFailureCount,
                 @CurrentFailureCount, 0, @FirstFailureAtUtc, @ConfirmedDownAtUtc,
                 @DetectionDelaySeconds, @LastFailureAtUtc, NULL,
                 @DownNotificationCreatedAtUtc, NULL, 0, @CreatedAtUtc, @UpdatedAtUtc);
            SELECT last_insert_rowid();
            """;
        AddParameter(insert, "@DeviceId", deviceId);
        AddParameter(insert, "@StartedAtUtc", ToStorageDate(firstFailureAtUtc));
        AddParameter(insert, "@InitialFailureCount", failureCount);
        AddParameter(insert, "@CurrentFailureCount", failureCount);
        AddParameter(insert, "@FirstFailureAtUtc", ToStorageDate(firstFailureAtUtc));
        AddParameter(insert, "@ConfirmedDownAtUtc", ToStorageDate(confirmedDownAtUtc));
        AddParameter(insert, "@DetectionDelaySeconds", Math.Max(0, (long)(confirmedDownAtUtc - firstFailureAtUtc).TotalSeconds));
        AddParameter(insert, "@LastFailureAtUtc", ToStorageDate(confirmedDownAtUtc));
        AddParameter(insert, "@DownNotificationCreatedAtUtc", downNotificationCreatedAtUtc.HasValue ? ToStorageDate(downNotificationCreatedAtUtc.Value) : null);
        AddParameter(insert, "@CreatedAtUtc", ToStorageDate(nowUtc));
        AddParameter(insert, "@UpdatedAtUtc", ToStorageDate(nowUtc));
        var result = await insert.ExecuteScalarAsync(cancellationToken);
        return Convert.ToInt64(result, CultureInfo.InvariantCulture);
    }

    private static async Task<DateTime?> GetFirstFailureForCurrentStreakAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        int deviceId,
        int failureCount,
        DateTime confirmedDownAtUtc,
        CancellationToken cancellationToken)
    {
        await using var select = connection.CreateCommand();
        select.Transaction = transaction;
        select.CommandText = """
            SELECT CheckedAt
            FROM PingLogs
            WHERE DeviceId = @DeviceId
              AND IsReachable = 0
              AND CheckedAt <= @ConfirmedDownAtUtc
            ORDER BY CheckedAt DESC
            LIMIT @Limit;
            """;
        AddParameter(select, "@DeviceId", deviceId);
        AddParameter(select, "@ConfirmedDownAtUtc", ToStorageDate(confirmedDownAtUtc));
        AddParameter(select, "@Limit", Math.Max(1, failureCount));

        DateTime? first = null;
        await using var reader = await select.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            first = FromStorageDate(reader.GetString(0));
        }

        return first;
    }

    private static NtfyNotificationPayload CreateDownPayload(
        PingDeviceResult result,
        PingLog log,
        NotificationSettings settings,
        long incidentId)
    {
        var lines = new List<string>
        {
            $"Cihaz: {result.Device.Name}"
        };
        if (settings.IncludeIpAddress)
        {
            lines.Add($"IP: {result.Device.IpAddress}");
        }

        lines.Add($"Tur: {result.Device.DeviceType.ToDisplayName()}");
        lines.Add($"Grup: {result.Device.GroupName}");
        lines.Add($"Kesinti baslangici: {log.CheckedAt.ToLocalTime():dd.MM.yyyy HH:mm:ss}");
        lines.Add($"Ardisik hata: {result.Device.ConsecutiveFailures + 1}");
        lines.Add($"Son hata: {Trim(result.ErrorMessage, 300)}");

        return new NtfyNotificationPayload
        {
            EventType = "DeviceDown",
            DeviceId = result.Device.Id,
            IncidentId = incidentId,
            Title = $"{result.Device.Name} erisilemiyor",
            Message = string.Join(Environment.NewLine, lines),
            Priority = "high",
            Tags = "warning,rotating_light"
        };
    }

    private static NtfyNotificationPayload CreateRecoveryPayload(
        PingDeviceResult result,
        PingLog log,
        NotificationSettings settings,
        long incidentId,
        DateTime startedAtUtc)
    {
        var duration = log.CheckedAt.ToUniversalTime() - startedAtUtc.ToUniversalTime();
        var lines = new List<string>
        {
            $"Cihaz: {result.Device.Name}"
        };
        if (settings.IncludeIpAddress)
        {
            lines.Add($"IP: {result.Device.IpAddress}");
        }

        lines.Add($"Duzelme zamani: {log.CheckedAt.ToLocalTime():dd.MM.yyyy HH:mm:ss}");
        lines.Add($"Kesinti suresi: {FormatDuration(duration)}");
        lines.Add($"Gecikme: {(result.LatencyMs.HasValue ? result.LatencyMs.Value.ToString(CultureInfo.InvariantCulture) : "-")} ms");

        return new NtfyNotificationPayload
        {
            EventType = "DeviceRecovered",
            DeviceId = result.Device.Id,
            IncidentId = incidentId,
            Title = $"{result.Device.Name} tekrar erisilebilir",
            Message = string.Join(Environment.NewLine, lines),
            Priority = "default",
            Tags = "white_check_mark"
        };
    }

    private static async Task<IncidentRow?> GetOpenIncidentAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        int deviceId,
        CancellationToken cancellationToken)
    {
        await using var select = connection.CreateCommand();
        select.Transaction = transaction;
        select.CommandText = """
            SELECT Id, StartedAtUtc, CurrentFailureCount, RecoverySuccessCount
            FROM DeviceIncidents
            WHERE DeviceId = @DeviceId
              AND Status = 'Open'
            LIMIT 1;
            """;
        AddParameter(select, "@DeviceId", deviceId);

        await using var reader = await select.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken))
        {
            return null;
        }

        return new IncidentRow(
            reader.GetInt64(0),
            FromStorageDate(reader.GetString(1)),
            reader.GetInt32(2),
            reader.GetInt32(3));
    }

    private static async Task<DateTime?> GetLastNotificationUtcAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        int deviceId,
        string eventType,
        CancellationToken cancellationToken)
    {
        await using var select = connection.CreateCommand();
        select.Transaction = transaction;
        select.CommandText = """
            SELECT MAX(CreatedAtUtc)
            FROM NotificationOutbox
            WHERE DeviceId = @DeviceId
              AND EventType = @EventType
              AND Status <> 'Cancelled';
            """;
        AddParameter(select, "@DeviceId", deviceId);
        AddParameter(select, "@EventType", eventType);
        var result = await select.ExecuteScalarAsync(cancellationToken);
        return result is null || result == DBNull.Value
            ? null
            : FromStorageDate((string)result);
    }

    public static string CreateDeduplicationKey(string eventType, int deviceId, long incidentId)
    {
        return $"{eventType}:device:{deviceId}:incident:{incidentId}".ToLowerInvariant();
    }

    private static string FormatDuration(TimeSpan value)
    {
        if (value.TotalDays >= 1)
        {
            return $"{(int)value.TotalDays} gun {value.Hours} sa {value.Minutes} dk";
        }

        if (value.TotalHours >= 1)
        {
            return $"{(int)value.TotalHours} sa {value.Minutes} dk";
        }

        return $"{Math.Max(0, (int)value.TotalMinutes)} dk {value.Seconds} sn";
    }

    private static string Trim(string value, int maxLength)
    {
        var text = value.ReplaceLineEndings(" ").Trim();
        return text.Length <= maxLength ? text : text[..maxLength];
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

    private readonly record struct IncidentRow(
        long Id,
        DateTime StartedAtUtc,
        int CurrentFailureCount,
        int RecoverySuccessCount);
}
