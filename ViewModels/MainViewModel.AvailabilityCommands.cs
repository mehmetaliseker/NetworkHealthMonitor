namespace NetworkHealthMonitor.ViewModels;

public sealed partial class MainViewModel
{
    private async Task RecalculateAvailabilityAsync()
    {
        IsBusy = true;
        StatusMessage = "Availability verileri yeniden hesaplaniyor...";
        try
        {
            var end = DateOnly.FromDateTime(DateTime.Now);
            var start = end.AddDays(-30);
            await _availabilityService.RecalculateDailyAsync(start, end, TimeZoneInfo.Local.Id);
            await LoadAvailabilityAsync();
            StatusMessage = "Availability verileri yeniden hesaplandi.";
        }
        catch (Exception ex)
        {
            _dialogService.ShowError("Availability yeniden hesaplanamadi", ex.Message);
            StatusMessage = "Availability yeniden hesaplanamadi.";
        }
        finally
        {
            IsBusy = false;
        }
    }
}
