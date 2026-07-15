using System.Collections;
using NetworkHealthMonitor.Models;

namespace NetworkHealthMonitor.ViewModels;

public sealed partial class MainViewModel
{
    private async Task LoadOutboxAsync()
    {
        var status = OutboxStatusFilter == AllOutboxStatusesText ? null : OutboxStatusFilter;
        var eventType = OutboxEventTypeFilter == AllOutboxEventTypesText ? null : OutboxEventTypeFilter;
        var startUtc = OutboxStartDate?.Date.ToUniversalTime();
        var endUtc = OutboxEndDate?.Date.AddDays(1).AddTicks(-1).ToUniversalTime();

        ReplaceCollection(
            NotificationOutboxItems,
            await _notificationOutboxRepository.GetFilteredAsync(status, eventType, OutboxDeviceFilterId, startUtc, endUtc, 1000));
        var counts = await _notificationOutboxRepository.GetCountsAsync();
        PendingNotificationCount = counts.Pending;
        FailedNotificationCount = counts.Failed;
        RaiseCommandStates();
    }

    private async Task RetrySelectedOutboxAsync(object? parameter)
    {
        var items = GetSelectedOutboxItems(parameter).Where(item => item.Status == "Failed").ToList();
        if (items.Count == 0)
        {
            _dialogService.ShowWarning("Kayit secilmedi", "Tekrar denenecek Failed kayit secin.");
            return;
        }

        WarnIfAuthorizationFailure(items);
        if (!_dialogService.Confirm("Failed bildirimler tekrar denensin mi?", $"{items.Count} failed kayit Pending durumuna alinacak. Worker bir sonraki dispatcher dongusunde gonderecek."))
        {
            return;
        }

        IsBusy = true;
        try
        {
            var affected = await _notificationOutboxRepository.RetryFailedAsync(items.Select(item => item.Id), DateTime.UtcNow);
            await LoadOutboxAsync();
            StatusMessage = $"{affected} failed bildirim tekrar denemeye alindi.";
        }
        finally
        {
            IsBusy = false;
        }
    }

    private async Task RetryOutboxAsync(NotificationOutboxItem? item)
    {
        if (item is not { Status: "Failed" })
        {
            return;
        }

        await RetrySelectedOutboxAsync(new[] { item });
    }

    private async Task CancelPendingOutboxAsync(NotificationOutboxItem? item)
    {
        if (item is not { Status: "Pending" })
        {
            return;
        }

        if (!_dialogService.Confirm("Pending bildirim iptal edilsin mi?", $"Outbox #{item.Id} Cancelled durumuna alinacak."))
        {
            return;
        }

        IsBusy = true;
        try
        {
            var affected = await _notificationOutboxRepository.CancelPendingAsync(new[] { item.Id }, DateTime.UtcNow);
            await LoadOutboxAsync();
            StatusMessage = $"{affected} pending bildirim iptal edildi.";
        }
        finally
        {
            IsBusy = false;
        }
    }

    private void ShowOutboxDetail(NotificationOutboxItem? item)
    {
        if (item is null)
        {
            return;
        }

        _dialogService.ShowInfo(
            $"Outbox #{item.Id}",
            $"""
            Durum: {item.Status}
            Bildirim turu: {item.EventType}
            Cihaz: {(string.IsNullOrWhiteSpace(item.DeviceName) ? "-" : item.DeviceName)}
            Incident: {item.IncidentText}
            Olusturulma: {item.CreatedAtText}
            Gonderilme: {item.SentAtText}
            Deneme sayisi: {item.AttemptCount}
            Son deneme: {item.LastAttemptAtText}
            Sonraki deneme: {item.NextAttemptAtText}

            Son hata:
            {item.LastError}

            Payload:
            {item.PayloadJson}
            """);
    }

    private List<NotificationOutboxItem> GetSelectedOutboxItems(object? parameter)
    {
        var selected = new List<NotificationOutboxItem>();
        if (parameter is IEnumerable selectedItems and not string)
        {
            selected.AddRange(selectedItems.OfType<NotificationOutboxItem>().Where(item => item.Id > 0));
        }

        selected.AddRange(NotificationOutboxItems.Where(item => item is { Id: > 0, IsSelected: true }));
        if (SelectedNotificationOutboxItem is { Id: > 0 })
        {
            selected.Add(SelectedNotificationOutboxItem);
        }

        return selected.DistinctBy(item => item.Id).ToList();
    }

    private void WarnIfAuthorizationFailure(IEnumerable<NotificationOutboxItem> items)
    {
        if (!items.Any(item =>
                item.LastError.Contains("401", StringComparison.OrdinalIgnoreCase)
                || item.LastError.Contains("403", StringComparison.OrdinalIgnoreCase)
                || item.LastError.Contains("Unauthorized", StringComparison.OrdinalIgnoreCase)
                || item.LastError.Contains("Forbidden", StringComparison.OrdinalIgnoreCase)))
        {
            return;
        }

        _dialogService.ShowWarning(
            "ntfy yetkilendirme hatasi",
            "Secili kayitlarda 401/403 hatasi var. Tekrar denemeden once ntfy URL, topic ve access token ayarlarini kontrol edin.");
    }
}
