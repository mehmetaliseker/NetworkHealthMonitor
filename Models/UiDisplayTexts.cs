namespace NetworkHealthMonitor.Models;

public static class UiDisplayTexts
{
    public static string OutboxStatus(string? status)
    {
        return (status ?? string.Empty).Trim() switch
        {
            "Pending" => "Bekliyor",
            "Processing" => "İşleniyor",
            "Sent" => "Gönderildi",
            "Failed" => "Başarısız",
            "DeadLetter" => "Kalıcı hata",
            "Cancelled" => "İptal edildi",
            _ => string.IsNullOrWhiteSpace(status) ? "-" : status
        };
    }

    public static string OutboxEventType(string? eventType)
    {
        return (eventType ?? string.Empty).Trim() switch
        {
            "DeviceSuspectedOffline" => "İlk kesinti bildirimi",
            "DeviceOfflineEscalated" => "Escalation bildirimi",
            "DeviceDown" => "Kesinti bildirimi",
            "DeviceRecovered" => "Düzelme bildirimi",
            "Test" => "Test bildirimi",
            _ => string.IsNullOrWhiteSpace(eventType) ? "-" : eventType
        };
    }

    public static string MaintenanceStatus(MaintenanceWindowStatus status)
    {
        return status switch
        {
            MaintenanceWindowStatus.Scheduled => "Planlandı",
            MaintenanceWindowStatus.Active => "Etkin",
            MaintenanceWindowStatus.Completed => "Tamamlandı",
            MaintenanceWindowStatus.Cancelled => "İptal edildi",
            _ => "Bilinmiyor"
        };
    }

    public static string ReadinessLevelText(ReadinessLevel level)
    {
        return level switch
        {
            ReadinessLevel.Pass => "Geçti",
            ReadinessLevel.Warning => "Uyarı",
            _ => "Başarısız"
        };
    }

    public static string ActiveState(bool isActive)
    {
        return isActive ? "Etkin" : "Devre dışı";
    }
}
