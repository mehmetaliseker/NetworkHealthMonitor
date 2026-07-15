using System.Text.Json;
using NetworkHealthMonitor.Data;
using NetworkHealthMonitor.Infrastructure;
using NetworkHealthMonitor.Models;

namespace NetworkHealthMonitor.Services;

public sealed class NotificationDispatcherService
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private readonly INotificationOutboxRepository _outboxRepository;
    private readonly INtfyNotificationClient _client;
    private readonly AppSettingsService _settingsService;
    private readonly IAlertPolicyService _alertPolicyService;
    private readonly WorkerHeartbeatRepository? _heartbeatRepository;
    private readonly string _workerInstanceId;

    public NotificationDispatcherService(
        INotificationOutboxRepository outboxRepository,
        INtfyNotificationClient client,
        AppSettingsService settingsService,
        IAlertPolicyService alertPolicyService,
        string workerInstanceId,
        WorkerHeartbeatRepository? heartbeatRepository = null)
    {
        _outboxRepository = outboxRepository;
        _client = client;
        _settingsService = settingsService;
        _alertPolicyService = alertPolicyService;
        _workerInstanceId = workerInstanceId;
        _heartbeatRepository = heartbeatRepository;
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
        if (!settings.Enabled)
        {
            return;
        }

        var nowUtc = DateTime.UtcNow;
        var items = await _outboxRepository.ClaimDueAsync(
            maxItems: 20,
            _workerInstanceId,
            nowUtc,
            TimeSpan.FromMinutes(5),
            cancellationToken);

        foreach (var item in items)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var payload = JsonSerializer.Deserialize<NtfyNotificationPayload>(item.PayloadJson, JsonOptions);
            if (payload is null)
            {
                await _outboxRepository.MarkFailedAsync(item.Id, item.AttemptCount + 1, "Invalid notification payload.", cancellationToken);
                continue;
            }

            var result = await _client.PublishAsync(settings, payload, cancellationToken);
            if (result.Success)
            {
                await _outboxRepository.MarkSentAsync(item.Id, DateTime.UtcNow, cancellationToken);
                if (_heartbeatRepository is not null)
                {
                    await _heartbeatRepository.MarkNotificationDispatchAsync(_workerInstanceId, DateTime.UtcNow, cancellationToken);
                }

                await UpdateNotificationSuccessAsync(cancellationToken);
                continue;
            }

            var nextAttempt = item.AttemptCount + 1;
            if (!result.IsTransient || nextAttempt > settings.MaxRetryCount)
            {
                await _outboxRepository.MarkFailedAsync(item.Id, nextAttempt, result.SafeErrorMessage, cancellationToken);
                await MarkNtfyExceptionAsync(result.SafeErrorMessage, cancellationToken);
                await UpdateNotificationFailureAsync(result.SafeErrorMessage, cancellationToken);
                continue;
            }

            await _outboxRepository.MarkRetryAsync(
                item.Id,
                nextAttempt,
                _alertPolicyService.CalculateNextRetryUtc(nextAttempt, settings.InitialRetryDelaySeconds, result.RetryAfter, DateTime.UtcNow),
                result.SafeErrorMessage,
                cancellationToken);
        }
    }

    private async Task UpdateNotificationSuccessAsync(CancellationToken cancellationToken)
    {
        var appSettings = await _settingsService.LoadAsync();
        appSettings.Notifications.LastSuccessfulNotificationAtUtc = DateTime.UtcNow;
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
