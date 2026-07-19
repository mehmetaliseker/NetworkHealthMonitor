using NetworkHealthMonitor.Data;
using NetworkHealthMonitor.Infrastructure;
using NetworkHealthMonitor.Models;

namespace NetworkHealthMonitor.Services;

public sealed class NotificationDispatcherService
{
    private readonly INotificationOutboxRepository _outboxRepository;
    private readonly IReadOnlyDictionary<string, INotificationChannel> _channels;
    private readonly AppSettingsService _settingsService;
    private readonly IAlertPolicyService _alertPolicyService;
    private readonly IDeviceOutageIncidentRepository? _incidentRepository;
    private readonly IClock _clock;
    private readonly WorkerHeartbeatRepository? _heartbeatRepository;
    private readonly string _workerInstanceId;

    public NotificationDispatcherService(
        INotificationOutboxRepository outboxRepository,
        INtfyNotificationClient client,
        AppSettingsService settingsService,
        IAlertPolicyService alertPolicyService,
        string workerInstanceId,
        WorkerHeartbeatRepository? heartbeatRepository = null)
        : this(
            outboxRepository,
            new INotificationChannel[]
            {
                new NtfyNotificationChannel(client),
                new EmailNotificationChannel(new SmtpEmailSender())
            },
            settingsService,
            alertPolicyService,
            workerInstanceId,
            heartbeatRepository,
            incidentRepository: null,
            clock: new SystemClock())
    {
    }

    public NotificationDispatcherService(
        INotificationOutboxRepository outboxRepository,
        IEnumerable<INotificationChannel> channels,
        AppSettingsService settingsService,
        IAlertPolicyService alertPolicyService,
        string workerInstanceId,
        WorkerHeartbeatRepository? heartbeatRepository = null,
        IDeviceOutageIncidentRepository? incidentRepository = null,
        IClock? clock = null)
    {
        _outboxRepository = outboxRepository;
        _channels = channels
            .GroupBy(channel => channel.Name, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.First(), StringComparer.OrdinalIgnoreCase);
        _settingsService = settingsService;
        _alertPolicyService = alertPolicyService;
        _workerInstanceId = workerInstanceId;
        _heartbeatRepository = heartbeatRepository;
        _incidentRepository = incidentRepository;
        _clock = clock ?? new SystemClock();
    }

    public async Task RunAsync(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                await DispatchOnceAsync(cancellationToken);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                AppErrorLogger.Log(ex, "NotificationDispatcher");
                await MarkNtfyExceptionAsync(ex.Message, cancellationToken);
            }

            await Task.Delay(TimeSpan.FromSeconds(10), cancellationToken);
        }
    }

    public async Task DispatchOnceAsync(CancellationToken cancellationToken = default)
    {
        var settings = (await _settingsService.LoadAsync()).Notifications;
        if (!settings.Enabled && !settings.EmailEnabled)
        {
            return;
        }

        var nowUtc = _clock.UtcNow;
        var items = await _outboxRepository.ClaimDueAsync(
            maxItems: 20,
            _workerInstanceId,
            nowUtc,
            TimeSpan.FromMinutes(5),
            cancellationToken);

        foreach (var item in items)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var channelName = string.IsNullOrWhiteSpace(item.Channel) ? NotificationChannels.Ntfy : item.Channel;
            if (!_channels.TryGetValue(channelName, out var channel))
            {
                await _outboxRepository.MarkDeadLetterAsync(item.Id, item.AttemptCount + 1, $"Unknown notification channel: {channelName}", cancellationToken);
                continue;
            }

            if (!channel.IsEnabled(settings))
            {
                await _outboxRepository.MarkRetryAsync(
                    item.Id,
                    item.AttemptCount,
                    _clock.UtcNow.AddMinutes(1),
                    $"{channel.Name} channel is disabled.",
                    cancellationToken);
                continue;
            }

            var result = await channel.SendAsync(item, settings, cancellationToken);
            if (result.Success)
            {
                var sentAtUtc = _clock.UtcNow;
                await _outboxRepository.MarkSentAsync(item.Id, sentAtUtc, cancellationToken);
                if (item.IncidentId.HasValue && _incidentRepository is not null)
                {
                    await _incidentRepository.MarkNotificationSentAsync(item.IncidentId.Value, item.EventType, sentAtUtc, cancellationToken);
                }

                if (_heartbeatRepository is not null)
                {
                    await _heartbeatRepository.MarkNotificationDispatchAsync(_workerInstanceId, sentAtUtc, cancellationToken);
                }

                await UpdateNotificationSuccessAsync(cancellationToken);
                AppErrorLogger.LogInfo($"Notification sent. OutboxId={item.Id}; EventType={item.EventType}; Channel={channel.Name}; DeviceId={item.DeviceId}; IncidentId={item.IncidentId}; Recipient={item.Recipient}");
                continue;
            }

            var nextAttempt = item.AttemptCount + 1;
            if (!result.IsTransient || nextAttempt > channel.MaxRetryCount(settings))
            {
                await _outboxRepository.MarkDeadLetterAsync(item.Id, nextAttempt, result.SafeErrorMessage, cancellationToken);
                await MarkNtfyExceptionAsync(result.SafeErrorMessage, cancellationToken);
                await UpdateNotificationFailureAsync(result.SafeErrorMessage, cancellationToken);
                AppErrorLogger.LogInfo($"Notification dead-lettered. OutboxId={item.Id}; EventType={item.EventType}; Channel={channel.Name}; DeviceId={item.DeviceId}; IncidentId={item.IncidentId}; Error={result.SafeErrorMessage}");
                continue;
            }

            await _outboxRepository.MarkRetryAsync(
                item.Id,
                nextAttempt,
                _alertPolicyService.CalculateNextRetryUtc(nextAttempt, channel.InitialRetryDelaySeconds(settings), result.RetryAfter, _clock.UtcNow),
                result.SafeErrorMessage,
                cancellationToken);
            AppErrorLogger.LogInfo($"Notification retry scheduled. OutboxId={item.Id}; EventType={item.EventType}; Channel={channel.Name}; Attempt={nextAttempt}; Error={result.SafeErrorMessage}");
        }
    }

    private async Task UpdateNotificationSuccessAsync(CancellationToken cancellationToken)
    {
        var appSettings = await _settingsService.LoadAsync();
        appSettings.Notifications.LastSuccessfulNotificationAtUtc = _clock.UtcNow;
        appSettings.Notifications.LastNotificationError = string.Empty;
        await _settingsService.SaveAsync(appSettings);
    }

    private async Task UpdateNotificationFailureAsync(string safeError, CancellationToken cancellationToken)
    {
        var appSettings = await _settingsService.LoadAsync();
        appSettings.Notifications.LastNotificationError = safeError;
        await _settingsService.SaveAsync(appSettings);
    }

    private async Task MarkNtfyExceptionAsync(string message, CancellationToken cancellationToken)
    {
        if (_heartbeatRepository is null)
        {
            return;
        }

        try
        {
            await _heartbeatRepository.MarkDiagnosticErrorAsync(_workerInstanceId, "LastNtfyException", message, cancellationToken);
        }
        catch
        {
            // Diagnostics must not break notification dispatch.
        }
    }
}
