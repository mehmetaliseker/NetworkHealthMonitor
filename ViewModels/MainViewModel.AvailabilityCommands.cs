namespace NetworkHealthMonitor.ViewModels;

public sealed partial class MainViewModel
{
    private async Task RecalculateAvailabilityAsync()
    {
        IsBusy = true;
        StatusMessage = "Erişilebilirlik verileri yeniden hesaplanıyor...";
        try
        {
            var end = DateOnly.FromDateTime(DateTime.Now);
            var start = end.AddDays(-30);
            await _availabilityService.RecalculateDailyAsync(start, end, TimeZoneInfo.Local.Id);
            await LoadAvailabilityAsync();
            StatusMessage = "Erişilebilirlik verileri yeniden hesaplandı.";
        }
        catch (Exception ex)
        {
            _dialogService.ShowError("Erişilebilirlik yeniden hesaplanamadı", ex.Message);
            StatusMessage = "Erişilebilirlik yeniden hesaplanamadı.";
        }
        finally
        {
            IsBusy = false;
        }
    }
}
