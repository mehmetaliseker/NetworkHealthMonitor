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
    private readonly INotificationTemplateRenderer _templateRenderer;
    private readonly IClock _clock;

    public IncidentService(
        SqliteConnectionFactory connectionFactory,
        AppSettingsService settingsService,
        IAlertPolicyService alertPolicyService,
        INotificationTemplateRenderer? templateRenderer = null,
        IClock? clock = null)
    {
        _connectionFactory = connectionFactory;
        _settingsService = settingsService;
        _alertPolicyService = alertPolicyService;
        _templateRenderer = templateRenderer ?? new NotificationTemplateRenderer();
        _clock = clock ?? new SystemClock();
    }

    public async Task ApplyPingResultsAsync(
        IReadOnlyList<PingDeviceResult> results,
        IReadOnlyDictionary<int, PingLog> logsByDeviceId,
        CancellationToken cancellationToken = default)
    {
        var settings = (await _settingsService.LoadAsync()).Notifications;
        var nowUtc = _clock.UtcNow;

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

        await QueueDueEscalationsAsync(connection, transaction, settings, nowUtc, cancellationToken);
        transaction.Commit();
    }

    public async Task EvaluateOpenIncidentsAsync(CancellationToken cancellationToken = default)
    {
        var settings = (await _settingsService.LoadAsync()).Notifications;
        var nowUtc = _clock.UtcNow;
        await using var connection = await _connectionFactory.CreateOpenConnectionAsync();
        using var transaction = connection.BeginTransaction();
        await QueueDueEscalationsAsync(connection, transaction, settings, nowUtc, cancellationToken);
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
        var observedAtUtc = log.CheckedAt.ToUniversalTime();
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
                    LastObservedAtUtc = @LastObservedAtUtc,
                    CurrentState = 'Open',
                    UpdatedAtUtc = @UpdatedAtUtc
                WHERE Id = @Id;
                """;
            AddParameter(update, "@CurrentFailureCount", Math.Max(failureCount, existing.Value.CurrentFailureCount + 1));
            AddParameter(update, "@LastFailureAtUtc", ToStorageDate(observedAtUtc));
            AddParameter(update, "@LastObservedAtUtc", ToStorageDate(observedAtUtc));
            AddParameter(update, "@UpdatedAtUtc", ToStorageDate(nowUtc));
            AddParameter(update, "@Id", existing.Value.Id);
            await update.ExecuteNonQueryAsync(cancellationToken);
            return;
        }

        if (result.Status != DeviceStatus.Offline)
        {
            return;
        }

        var confirmedDownAtUtc = observedAtUtc;
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
            result.Device.LastSuccessfulCheckAt?.ToUniversalTime(),
            nowUtc,
            cancellationToken);

        AppErrorLogger.LogInfo($"Device outage incident opened. DeviceId={result.Device.Id}; IncidentId={incidentId}; StartedAtUtc={firstFailureAtUtc:O}; ConfirmedDownAtUtc={confirmedDownAtUtc:O}");

        if (IsNotificationSuppressed(result.Device, confirmedDownAtUtc))
        {
            AppErrorLogger.LogInfo($"Initial notification suppressed. DeviceId={result.Device.Id}; IncidentId={incidentId}; Mode={result.Device.SuppressionMode}");
            return;
        }

        await QueueIncidentNotificationsAsync(
            connection,
            transaction,
            NotificationEventTypes.DeviceSuspectedOffline,
            result,
            log,
            settings,
            incidentId,
            firstFailureAtUtc,
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

        var observedAtUtc = log.CheckedAt.ToUniversalTime();
        var recoverySuccessCount = existing.Value.RecoverySuccessCount + 1;
        if (recoverySuccessCount < settings.RecoverySuccessThreshold)
        {
            await using var updateProgress = connection.CreateCommand();
            updateProgress.Transaction = transaction;
            updateProgress.CommandText = """
                UPDATE DeviceIncidents
                SET RecoverySuccessCount = @RecoverySuccessCount,
                    LastSuccessAtUtc = @LastSuccessAtUtc,
                    LastSuccessfulCheckAtUtc = @LastSuccessfulCheckAtUtc,
                    LastObservedAtUtc = @LastObservedAtUtc,
                    UpdatedAtUtc = @UpdatedAtUtc
                WHERE Id = @Id;
                """;
            AddParameter(updateProgress, "@RecoverySuccessCount", recoverySuccessCount);
            AddParameter(updateProgress, "@LastSuccessAtUtc", ToStorageDate(observedAtUtc));
            AddParameter(updateProgress, "@LastSuccessfulCheckAtUtc", ToStorageDate(observedAtUtc));
            AddParameter(updateProgress, "@LastObservedAtUtc", ToStorageDate(observedAtUtc));
            AddParameter(updateProgress, "@UpdatedAtUtc", ToStorageDate(nowUtc));
            AddParameter(updateProgress, "@Id", existing.Value.Id);
            await updateProgress.ExecuteNonQueryAsync(cancellationToken);
            return;
        }

        var allowRecoveryNotification = settings.NotifyOnDeviceRecovered || settings.EmailNotifyOnDeviceRecovered;
        await using var close = connection.CreateCommand();
        close.Transaction = transaction;
        close.CommandText = """
            UPDATE DeviceIncidents
            SET Status = 'Closed',
                CurrentState = 'Closed',
                EndedAtUtc = @EndedAtUtc,
                ResolvedAtUtc = @ResolvedAtUtc,
                RecoverySuccessCount = @RecoverySuccessCount,
                LastSuccessAtUtc = @LastSuccessAtUtc,
                LastSuccessfulCheckAtUtc = @LastSuccessfulCheckAtUtc,
                LastObservedAtUtc = @LastObservedAtUtc,
                UpdatedAtUtc = @UpdatedAtUtc
            WHERE Id = @Id;
            """;
        AddParameter(close, "@EndedAtUtc", ToStorageDate(observedAtUtc));
        AddParameter(close, "@ResolvedAtUtc", ToStorageDate(observedAtUtc));
        AddParameter(close, "@RecoverySuccessCount", recoverySuccessCount);
        AddParameter(close, "@LastSuccessAtUtc", ToStorageDate(observedAtUtc));
        AddParameter(close, "@LastSuccessfulCheckAtUtc", ToStorageDate(observedAtUtc));
        AddParameter(close, "@LastObservedAtUtc", ToStorageDate(observedAtUtc));
        AddParameter(close, "@UpdatedAtUtc", ToStorageDate(nowUtc));
        AddParameter(close, "@Id", existing.Value.Id);
        await close.ExecuteNonQueryAsync(cancellationToken);

        AppErrorLogger.LogInfo($"Device outage incident closed. DeviceId={result.Device.Id}; IncidentId={existing.Value.Id}; ResolvedAtUtc={observedAtUtc:O}");

        if (!allowRecoveryNotification || IsNotificationSuppressed(result.Device, observedAtUtc))
        {
            return;
        }

        await QueueIncidentNotificationsAsync(
            connection,
            transaction,
            NotificationEventTypes.DeviceRecovered,
            result,
            log,
            settings,
            existing.Value.Id,
            existing.Value.StartedAtUtc,
            nowUtc,
            cancellationToken);
    }

    private async Task QueueDueEscalationsAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        NotificationSettings settings,
        DateTime nowUtc,
        CancellationToken cancellationToken)
    {
        if (!settings.NotifyOnDeviceEscalated || (!settings.EmailEnabled && !settings.Enabled))
        {
            return;
        }

        var incidents = await GetOpenEscalationCandidatesAsync(connection, transaction, cancellationToken);
        var threshold = TimeSpan.FromHours(settings.EscalationThresholdHours);
        foreach (var candidate in incidents)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (IsNotificationSuppressed(candidate.Device, nowUtc))
            {
                continue;
            }

            var currentSuppressedSeconds = CalculateCurrentSuppressedSeconds(candidate, nowUtc);
            var effectiveOfflineDuration = nowUtc - candidate.StartedAtUtc - TimeSpan.FromSeconds(candidate.SuppressedDurationSeconds + currentSuppressedSeconds);
            if (effectiveOfflineDuration < threshold)
            {
                continue;
            }

            var log = new PingLog
            {
                DeviceId = candidate.Device.Id,
                DeviceName = candidate.Device.Name,
                IpAddress = candidate.Device.IpAddress,
                DeviceType = candidate.Device.DeviceType,
                GroupName = candidate.Device.GroupName,
                Status = DeviceStatus.Offline,
                IsReachable = false,
                CheckedAt = candidate.LastObservedAtUtc,
                TriggerType = PingTriggerType.Scheduled
            };
            var result = new PingDeviceResult(
                candidate.Device,
                false,
                null,
                candidate.LastObservedAtUtc,
                string.Empty,
                "Device remains offline.",
                DeviceStatus.Offline);

            await QueueIncidentNotificationsAsync(
                connection,
                transaction,
                NotificationEventTypes.DeviceOfflineEscalated,
                result,
                log,
                settings,
                candidate.Id,
                candidate.StartedAtUtc,
                nowUtc,
                cancellationToken);
        }
    }

    private async Task QueueIncidentNotificationsAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        string eventType,
        PingDeviceResult result,
        PingLog log,
        NotificationSettings settings,
        long incidentId,
        DateTime incidentStartedAtUtc,
        DateTime nowUtc,
        CancellationToken cancellationToken)
    {
        if (ShouldQueueNtfy(eventType, settings))
        {
            var payload = CreateNtfyPayload(eventType, result, log, settings, incidentId, incidentStartedAtUtc, nowUtc);
            await NotificationOutboxRepository.AddPendingAsync(
                connection,
                transaction,
                new NotificationOutboxCreateRequest
                {
                    EventType = eventType,
                    DeviceId = result.Device.Id,
                    IncidentId = incidentId,
                    Channel = NotificationChannels.Ntfy,
                    Recipient = settings.Topic,
                    Subject = payload.Title,
                    Body = payload.Message,
                    PayloadJson = JsonSerializer.Serialize(payload, JsonOptions),
                    IdempotencyKey = CreateIdempotencyKey(eventType, NotificationChannels.Ntfy, settings.Topic, result.Device.Id, incidentId)
                },
                nowUtc,
                cancellationToken);
            AppErrorLogger.LogInfo($"Notification queued. EventType={eventType}; Channel=Ntfy; DeviceId={result.Device.Id}; IncidentId={incidentId}");
        }

        foreach (var request in CreateEmailRequests(eventType, result, log, settings, incidentId, incidentStartedAtUtc, nowUtc))
        {
            await NotificationOutboxRepository.AddPendingAsync(
                connection,
                transaction,
                request,
                nowUtc,
                cancellationToken);
            AppErrorLogger.LogInfo($"Notification queued. EventType={eventType}; Channel=Email; DeviceId={result.Device.Id}; IncidentId={incidentId}; Recipient={request.Recipient}");
        }
    }

    private IEnumerable<NotificationOutboxCreateRequest> CreateEmailRequests(
        string eventType,
        PingDeviceResult result,
        PingLog log,
        NotificationSettings settings,
        long incidentId,
        DateTime incidentStartedAtUtc,
        DateTime nowUtc)
    {
        if (!ShouldQueueEmail(eventType, settings))
        {
            yield break;
        }

        var recipients = eventType == NotificationEventTypes.DeviceOfflineEscalated
            ? settings.EscalationEmailRecipients
            : settings.InitialEmailRecipients;
        if (recipients.Count == 0)
        {
            yield break;
        }

        var threshold = TimeSpan.FromHours(settings.EscalationThresholdHours);
        var context = new NotificationTemplateContext
        {
            DeviceName = result.Device.Name,
            IpAddress = result.Device.IpAddress,
            DeviceType = result.Device.DeviceType.ToDisplayName(),
            GroupName = result.Device.GroupName,
            Status = result.Status.ToDisplayName(),
            IncidentStartedAtUtc = incidentStartedAtUtc,
            LastSuccessfulCheckAtUtc = result.Device.LastSuccessfulCheckAt?.ToUniversalTime(),
            LastCheckAtUtc = log.CheckedAt.ToUniversalTime(),
            OfflineDuration = nowUtc - incidentStartedAtUtc,
            EscalationThreshold = threshold
        };

        var (subjectTemplate, bodyTemplate) = eventType switch
        {
            NotificationEventTypes.DeviceOfflineEscalated => (settings.EmailTemplates.EscalationSubject, settings.EmailTemplates.EscalationBody),
            NotificationEventTypes.DeviceRecovered => (settings.EmailTemplates.RecoveredSubject, settings.EmailTemplates.RecoveredBody),
            _ => (settings.EmailTemplates.InitialOfflineSubject, settings.EmailTemplates.InitialOfflineBody)
        };

        string subject;
        string body;
        try
        {
            subject = _templateRenderer.Render(subjectTemplate, context).Trim();
            body = _templateRenderer.Render(bodyTemplate, context);
        }
        catch (Exception ex)
        {
            AppErrorLogger.Log(ex, $"Notification template render failed. EventType={eventType}; DeviceId={result.Device.Id}; IncidentId={incidentId}");
            yield break;
        }

        if (string.IsNullOrWhiteSpace(subject))
        {
            AppErrorLogger.LogInfo($"Notification template render failed. Empty subject. EventType={eventType}; DeviceId={result.Device.Id}; IncidentId={incidentId}");
            yield break;
        }

        foreach (var recipient in recipients.Where(recipient => EmailAddressValidator.IsValid(recipient.Email)).DistinctBy(recipient => recipient.NormalizedEmail))
        {
            yield return new NotificationOutboxCreateRequest
            {
                EventType = eventType,
                DeviceId = result.Device.Id,
                IncidentId = incidentId,
                Channel = NotificationChannels.Email,
                Recipient = recipient.Email.Trim(),
                Subject = subject,
                Body = body,
                PayloadJson = "{}",
                IdempotencyKey = CreateIdempotencyKey(eventType, NotificationChannels.Email, recipient.NormalizedEmail, result.Device.Id, incidentId)
            };
        }
    }

    private static bool ShouldQueueNtfy(string eventType, NotificationSettings settings)
    {
        if (!settings.Enabled)
        {
            return false;
        }

        return eventType switch
        {
            NotificationEventTypes.DeviceSuspectedOffline => settings.NotifyOnDeviceDown,
            NotificationEventTypes.DeviceOfflineEscalated => settings.NotifyOnDeviceEscalated,
            NotificationEventTypes.DeviceRecovered => settings.NotifyOnDeviceRecovered,
            _ => false
        };
    }

    private static bool ShouldQueueEmail(string eventType, NotificationSettings settings)
    {
        if (!settings.EmailEnabled)
        {
            return false;
        }

        return eventType switch
        {
            NotificationEventTypes.DeviceSuspectedOffline => settings.NotifyOnDeviceDown,
            NotificationEventTypes.DeviceOfflineEscalated => settings.NotifyOnDeviceEscalated,
            NotificationEventTypes.DeviceRecovered => settings.EmailNotifyOnDeviceRecovered,
            _ => false
        };
    }

    private static bool IsNotificationSuppressed(Device device, DateTime observedAtUtc)
    {
        return device.SuppressionMode != DeviceSuppressionMode.None
            && (!device.SuppressedUntilUtc.HasValue || device.SuppressedUntilUtc.Value.ToUniversalTime() > observedAtUtc.ToUniversalTime());
    }

    private static long CalculateCurrentSuppressedSeconds(EscalationCandidate candidate, DateTime nowUtc)
    {
        if (!IsNotificationSuppressed(candidate.Device, nowUtc))
        {
            return 0;
        }

        var suppressedFromUtc = candidate.Device.SuppressedFromUtc?.ToUniversalTime() ?? candidate.StartedAtUtc;
        var effectiveStart = suppressedFromUtc > candidate.StartedAtUtc ? suppressedFromUtc : candidate.StartedAtUtc;
        return Math.Max(0, (long)(nowUtc.ToUniversalTime() - effectiveStart).TotalSeconds);
    }

    private static NtfyNotificationPayload CreateNtfyPayload(
        string eventType,
        PingDeviceResult result,
        PingLog log,
        NotificationSettings settings,
        long incidentId,
        DateTime incidentStartedAtUtc,
        DateTime nowUtc)
    {
        var title = eventType switch
        {
            NotificationEventTypes.DeviceOfflineEscalated => $"{result.Device.Name} uzun suredir erisilemiyor",
            NotificationEventTypes.DeviceRecovered => $"{result.Device.Name} tekrar erisilebilir",
            _ => $"{result.Device.Name} muhtemelen erisilemiyor"
        };
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
        lines.Add($"Durum: {result.Status.ToDisplayName()}");
        lines.Add($"Olay baslangici: {incidentStartedAtUtc.ToLocalTime():dd.MM.yyyy HH:mm:ss}");
        lines.Add($"Son kontrol: {log.CheckedAt.ToLocalTime():dd.MM.yyyy HH:mm:ss}");
        if (eventType == NotificationEventTypes.DeviceOfflineEscalated)
        {
            lines.Add($"Erisilememe suresi: {FormatDuration(nowUtc - incidentStartedAtUtc)}");
            lines.Add($"Escalation esigi: {FormatDuration(TimeSpan.FromHours(settings.EscalationThresholdHours))}");
        }
        else if (eventType == NotificationEventTypes.DeviceRecovered)
        {
            lines.Add($"Kesinti suresi: {FormatDuration(log.CheckedAt.ToUniversalTime() - incidentStartedAtUtc)}");
        }
        else
        {
            lines.Add($"Ardisik hata: {result.Device.ConsecutiveFailures + 1}");
            lines.Add($"Son hata: {Trim(result.ErrorMessage, 300)}");
        }

        return new NtfyNotificationPayload
        {
            EventType = eventType,
            DeviceId = result.Device.Id,
            IncidentId = incidentId,
            Title = title,
            Message = string.Join(Environment.NewLine, lines),
            Priority = eventType == NotificationEventTypes.DeviceRecovered ? "default" : "high",
            Tags = eventType == NotificationEventTypes.DeviceRecovered ? "white_check_mark" : "warning,rotating_light"
        };
    }

    private static async Task<long> InsertIncidentAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        int deviceId,
        DateTime firstFailureAtUtc,
        DateTime confirmedDownAtUtc,
        int failureCount,
        DateTime? lastSuccessfulCheckAtUtc,
        DateTime nowUtc,
        CancellationToken cancellationToken)
    {
        await using var insert = connection.CreateCommand();
        insert.Transaction = transaction;
        insert.CommandText = """
            INSERT INTO DeviceIncidents
                (DeviceId, StartedAtUtc, EndedAtUtc, Status, InitialFailureCount,
                 CurrentFailureCount, RecoverySuccessCount, FirstFailureAtUtc, ConfirmedDownAtUtc,
                 DetectionDelaySeconds, LastFailureAtUtc, LastObservedAtUtc, LastSuccessAtUtc,
                 LastSuccessfulCheckAtUtc, DownNotificationCreatedAtUtc, InitialNotificationSentAtUtc,
                 EscalationNotificationSentAtUtc, RecoveryNotificationCreatedAtUtc, ResolvedAtUtc,
                 CurrentState, SuppressedDurationSeconds, FlapCount, CreatedAtUtc, UpdatedAtUtc)
            VALUES
                (@DeviceId, @StartedAtUtc, NULL, 'Open', @InitialFailureCount,
                 @CurrentFailureCount, 0, @FirstFailureAtUtc, @ConfirmedDownAtUtc,
                 @DetectionDelaySeconds, @LastFailureAtUtc, @LastObservedAtUtc, NULL,
                 @LastSuccessfulCheckAtUtc, NULL, NULL, NULL, NULL, NULL,
                 'Open', 0, 0, @CreatedAtUtc, @UpdatedAtUtc);
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
        AddParameter(insert, "@LastObservedAtUtc", ToStorageDate(confirmedDownAtUtc));
        AddParameter(insert, "@LastSuccessfulCheckAtUtc", lastSuccessfulCheckAtUtc.HasValue ? ToStorageDate(lastSuccessfulCheckAtUtc.Value) : null);
        AddParameter(insert, "@CreatedAtUtc", ToStorageDate(nowUtc));
        AddParameter(insert, "@UpdatedAtUtc", ToStorageDate(nowUtc));
        var value = await insert.ExecuteScalarAsync(cancellationToken);
        return Convert.ToInt64(value, CultureInfo.InvariantCulture);
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

    private static async Task<IReadOnlyList<EscalationCandidate>> GetOpenEscalationCandidatesAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        CancellationToken cancellationToken)
    {
        var candidates = new List<EscalationCandidate>();
        await using var select = connection.CreateCommand();
        select.Transaction = transaction;
        select.CommandText = """
            SELECT i.Id, i.StartedAtUtc, COALESCE(i.LastObservedAtUtc, i.LastFailureAtUtc, i.ConfirmedDownAtUtc, i.StartedAtUtc),
                   i.SuppressedDurationSeconds,
                   d.Id, d.Name, d.IpAddress, d.DeviceType, d.Location, d.GroupId, d.GroupName, d.IsCritical,
                   d.IsActive, d.IsEnabled, d.IsDeleted, d.DeletedAtUtc, d.AutoCheckEnabled, d.DefaultSchedulePlanId,
                   d.PingTimeoutMs, d.CheckIntervalSeconds, d.FailureRetryIntervalSeconds, d.FailureRetryLimit,
                   d.FailureThreshold, d.Description, d.LastStatus, d.LastLatencyMs, d.LastCheckedAt,
                   d.LastSuccessfulCheckAt, d.LastFailedCheckAt, d.ConsecutiveFailures, d.ConsecutiveSuccesses,
                   d.LastStableStatus, d.SuppressionMode, d.SuppressedFromUtc, d.SuppressedUntilUtc,
                   d.SuppressionReason, d.SuppressedBy, d.CreatedAt, d.UpdatedAt, d.TargetAvailabilityPercent
            FROM DeviceIncidents i
            JOIN Devices d ON d.Id = i.DeviceId
            WHERE i.Status = 'Open'
              AND i.EscalationNotificationSentAtUtc IS NULL
              AND d.IsDeleted = 0;
            """;

        await using var reader = await select.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            candidates.Add(new EscalationCandidate(
                reader.GetInt64(0),
                FromStorageDate(reader.GetString(1)),
                FromStorageDate(reader.GetString(2)),
                reader.IsDBNull(3) ? 0 : reader.GetInt64(3),
                ReadDevice(reader, 4)));
        }

        return candidates;
    }

    public static string CreateDeduplicationKey(string eventType, int deviceId, long incidentId)
    {
        return $"{eventType}:device:{deviceId}:incident:{incidentId}".ToLowerInvariant();
    }

    public static string CreateIdempotencyKey(
        string eventType,
        string channel,
        string recipient,
        int deviceId,
        long incidentId)
    {
        return $"{eventType}:{channel}:device:{deviceId}:incident:{incidentId}:recipient:{recipient}".ToLowerInvariant();
    }

    private static Device ReadDevice(SqliteDataReader reader, int offset)
    {
        return new Device
        {
            Id = reader.GetInt32(offset),
            Name = reader.GetString(offset + 1),
            IpAddress = reader.GetString(offset + 2),
            DeviceType = DeviceTypeExtensions.FromStorageValue(reader.GetString(offset + 3)),
            Location = reader.GetString(offset + 4),
            GroupId = reader.IsDBNull(offset + 5) ? null : reader.GetInt32(offset + 5),
            GroupName = reader.GetString(offset + 6),
            IsCritical = reader.GetInt32(offset + 7) == 1,
            IsActive = reader.GetInt32(offset + 8) == 1,
            IsEnabled = reader.GetInt32(offset + 9) == 1,
            IsDeleted = reader.GetInt32(offset + 10) == 1,
            DeletedAtUtc = reader.IsDBNull(offset + 11) ? null : FromStorageDate(reader.GetString(offset + 11)),
            AutoCheckEnabled = reader.GetInt32(offset + 12) == 1,
            DefaultSchedulePlanId = reader.IsDBNull(offset + 13) ? null : reader.GetInt32(offset + 13),
            PingTimeoutMs = reader.IsDBNull(offset + 14) ? null : reader.GetInt32(offset + 14),
            CheckIntervalSeconds = reader.GetInt32(offset + 15),
            FailureRetryIntervalSeconds = reader.GetInt32(offset + 16),
            FailureRetryLimit = reader.GetInt32(offset + 17),
            FailureThreshold = reader.GetInt32(offset + 18),
            Description = reader.GetString(offset + 19),
            LastStatus = DeviceStatusExtensions.FromStorageValue(reader.GetString(offset + 20)),
            LastLatencyMs = reader.IsDBNull(offset + 21) ? null : reader.GetInt64(offset + 21),
            LastCheckedAt = reader.IsDBNull(offset + 22) ? null : FromStorageDate(reader.GetString(offset + 22)),
            LastSuccessfulCheckAt = reader.IsDBNull(offset + 23) ? null : FromStorageDate(reader.GetString(offset + 23)),
            LastFailedCheckAt = reader.IsDBNull(offset + 24) ? null : FromStorageDate(reader.GetString(offset + 24)),
            ConsecutiveFailures = reader.GetInt32(offset + 25),
            ConsecutiveSuccesses = reader.GetInt32(offset + 26),
            LastStableStatus = DeviceStatusExtensions.FromStorageValue(reader.GetString(offset + 27)),
            SuppressionMode = DeviceSuppressionModeExtensions.FromStorageValue(reader.GetString(offset + 28)),
            SuppressedFromUtc = reader.IsDBNull(offset + 29) ? null : FromStorageDate(reader.GetString(offset + 29)),
            SuppressedUntilUtc = reader.IsDBNull(offset + 30) ? null : FromStorageDate(reader.GetString(offset + 30)),
            SuppressionReason = reader.GetString(offset + 31),
            SuppressedBy = reader.GetString(offset + 32),
            CreatedAt = FromStorageDate(reader.GetString(offset + 33)),
            UpdatedAt = FromStorageDate(reader.GetString(offset + 34)),
            SlaTargetAvailabilityPercent = reader.IsDBNull(offset + 35) ? null : reader.GetDouble(offset + 35)
        };
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
        var text = (value ?? string.Empty).ReplaceLineEndings(" ").Trim();
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

    private readonly record struct EscalationCandidate(
        long Id,
        DateTime StartedAtUtc,
        DateTime LastObservedAtUtc,
        long SuppressedDurationSeconds,
        Device Device);
}
