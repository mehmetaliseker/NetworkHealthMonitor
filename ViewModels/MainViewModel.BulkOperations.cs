using System.Collections;
using NetworkHealthMonitor.Models;

namespace NetworkHealthMonitor.ViewModels;

public sealed partial class MainViewModel
{
    private async Task BulkSetAutoCheckAsync(object? parameter, bool enabled)
    {
        var devices = GetSelectedDevices(parameter);
        if (devices.Count == 0)
        {
            _dialogService.ShowWarning("Cihaz seçilmedi", "Toplu işlem için cihaz seçin.");
            return;
        }

        if (!_dialogService.Confirm(
                "Otomatik kontrol güncellensin mi?",
                $"{devices.Count} cihaz için otomatik kontrol {(enabled ? "açılacak" : "kapatılacak")}."))
        {
            return;
        }

        IsBusy = true;
        try
        {
            var affected = await _deviceRepository.BulkSetAutoCheckAsync(devices.Select(device => device.Id), enabled);
            await ReloadAllAsync();
            StatusMessage = $"{affected} cihaz için otomatik kontrol {(enabled ? "açıldı" : "kapatıldı")}.";
        }
        finally
        {
            IsBusy = false;
        }
    }

    private async Task BulkAssignGroupAsync(object? parameter)
    {
        var devices = GetSelectedDevices(parameter);
        if (devices.Count == 0)
        {
            _dialogService.ShowWarning("Cihaz seçilmedi", "Toplu işlem için cihaz seçin.");
            return;
        }

        var groupName = ResolveGroupName(BulkTargetGroupId);
        var targetText = string.IsNullOrWhiteSpace(groupName) ? "grupsuz yapılacak" : $"{groupName} grubuna atanacak";
        if (!_dialogService.Confirm("Seçili cihazlar gruba atansın mı?", $"{devices.Count} cihaz {targetText}."))
        {
            return;
        }

        IsBusy = true;
        try
        {
            var affected = await _deviceRepository.BulkSetGroupAsync(devices.Select(device => device.Id), BulkTargetGroupId, groupName);
            await ReloadAllAsync();
            StatusMessage = $"{affected} cihaz {(string.IsNullOrWhiteSpace(groupName) ? "grupsuz yapıldı" : $"{groupName} grubuna atandı")}.";
        }
        finally
        {
            IsBusy = false;
        }
    }

    private async Task BulkApplyCheckIntervalAsync(object? parameter)
    {
        var devices = GetSelectedDevices(parameter);
        if (devices.Count == 0)
        {
            _dialogService.ShowWarning("Cihaz seçilmedi", "Toplu işlem için cihaz seçin.");
            return;
        }

        var intervalText = BulkCheckIntervalSeconds <= 0
            ? "özel kontrol aralığı kaldırılacak ve grup/tip/global politika kullanılacak"
            : $"kontrol aralığı {BulkCheckIntervalSeconds} sn yapılacak";
        if (!_dialogService.Confirm("Kontrol aralığı güncellensin mi?", $"{devices.Count} cihaz için {intervalText}."))
        {
            return;
        }

        IsBusy = true;
        try
        {
            var affected = await _deviceRepository.BulkSetCheckIntervalAsync(devices.Select(device => device.Id), BulkCheckIntervalSeconds);
            await ReloadAllAsync();
            StatusMessage = BulkCheckIntervalSeconds <= 0
                ? $"{affected} cihaz için özel kontrol aralığı kaldırıldı; grup/tip/global politika kullanılacak."
                : $"{affected} cihaz için kontrol aralığı {BulkCheckIntervalSeconds} sn olarak güncellendi.";
        }
        finally
        {
            IsBusy = false;
        }
    }

    private async Task BulkDeactivateAsync(object? parameter)
    {
        var devices = GetSelectedDevices(parameter);
        if (devices.Count == 0)
        {
            _dialogService.ShowWarning("Cihaz seçilmedi", "Toplu işlem için cihaz seçin.");
            return;
        }

        if (!_dialogService.Confirm("Seçili cihazlar pasifleştirilsin mi?", $"{devices.Count} cihaz otomatik kontrol ve manuel toplu işlemler dışında kalacak. Cihaz kayıtları silinmez."))
        {
            return;
        }

        IsBusy = true;
        try
        {
            var affected = await _deviceRepository.BulkSetActiveAsync(devices.Select(device => device.Id), false);
            await ReloadAllAsync();
            StatusMessage = $"{affected} cihaz pasifleştirildi.";
        }
        finally
        {
            IsBusy = false;
        }
    }

    private bool CanUseSelectedDevices(object? parameter)
    {
        return !IsBusy && GetSelectedDevices(parameter).Count > 0;
    }

    private List<Device> GetSelectedDevices(object? parameter)
    {
        if (parameter is IEnumerable selectedItems and not string)
        {
            return selectedItems
                .OfType<Device>()
                .Where(device => device.Id > 0)
                .DistinctBy(device => device.Id)
                .ToList();
        }

        return SelectedDevice is { Id: > 0 }
            ? new List<Device> { SelectedDevice }
            : new List<Device>();
    }
}

