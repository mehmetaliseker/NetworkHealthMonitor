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
        var items = GetSelectedOutboxItems(parameter).Where(item => item.Status is "Failed" or "DeadLetter").ToList();
        if (items.Count == 0)
        {
            _dialogService.ShowWarning("Kayıt seçilmedi", "Yeniden denenecek başarısız veya kalıcı hata kaydı seçin.");
            return;
        }

        WarnIfAuthorizationFailure(items);
        if (!_dialogService.Confirm(
                "Başarısız bildirimler yeniden denensin mi?",
                $"{items.Count} başarısız kayıt bekliyor durumuna alınacak. İzleme servisi bir sonraki bildirim döngüsünde gönderecektir."))
        {
            return;
        }

        IsBusy = true;
        try
        {
            var affected = await _notificationOutboxRepository.RetryFailedAsync(items.Select(item => item.Id), DateTime.UtcNow);
            await LoadOutboxAsync();
            StatusMessage = $"{affected} başarısız bildirim yeniden denemeye alındı.";
        }
        finally
        {
            IsBusy = false;
        }
    }

    private async Task RetryOutboxAsync(NotificationOutboxItem? item)
    {
        if (item is not { Status: "Failed" } and not { Status: "DeadLetter" })
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

        if (!_dialogService.Confirm(
                "Bekleyen bildirim iptal edilsin mi?",
                $"Bildirim kuyruğu #{item.Id} iptal edildi durumuna alınacaktır."))
        {
            return;
        }

        IsBusy = true;
        try
        {
            var affected = await _notificationOutboxRepository.CancelPendingAsync(new[] { item.Id }, DateTime.UtcNow);
            await LoadOutboxAsync();
            StatusMessage = $"{affected} bekleyen bildirim iptal edildi.";
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
            $"Bildirim kuyruğu #{item.Id}",
            $"""
            Durum: {item.StatusText}
            Bildirim türü: {item.EventTypeText}
            Kanal: {item.ChannelText}
            Alici: {(string.IsNullOrWhiteSpace(item.Recipient) ? "-" : item.Recipient)}
            Konu: {(string.IsNullOrWhiteSpace(item.Subject) ? "-" : item.Subject)}
            Cihaz: {(string.IsNullOrWhiteSpace(item.DeviceName) ? "-" : item.DeviceName)}
            Kesinti: {item.IncidentText}
            Oluşturulma: {item.CreatedAtText}
            Gönderilme: {item.SentAtText}
            Deneme sayısı: {item.AttemptCount}
            Son deneme: {item.LastAttemptAtText}
            Sonraki deneme: {item.NextAttemptAtText}

            Son hata:
            {item.LastError}

            İçerik:
            {(string.IsNullOrWhiteSpace(item.Body) ? item.PayloadJson : item.Body)}
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
            "ntfy yetkilendirme hatası",
            "Seçili kayıtlarda 401/403 hatası var. Yeniden denemeden önce sunucu adresi, konu ve erişim anahtarı ayarlarını kontrol edin.");
    }
}
