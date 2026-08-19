using System.Collections;
using NetworkHealthMonitor.Models;

namespace NetworkHealthMonitor.ViewModels;

public sealed partial class MainViewModel
{
    private async Task SetDeviceSuppressionAsync(Device? device, DeviceSuppressionMode mode)
    {
        if (device is null)
        {
            return;
        }

        var untilUtc = BulkSuppressionDurationHours <= 0
            ? (DateTime?)null
            : DateTime.UtcNow.AddHours(BulkSuppressionDurationHours);
        var modeText = mode == DeviceSuppressionMode.PauseMonitoring
            ? "izleme geçici durdurulacak"
            : "bildirimler susturulacak";
        var durationText = untilUtc.HasValue ? $"{BulkSuppressionDurationHours} saat" : "süresiz";

        if (!_dialogService.Confirm(
                "Cihazda geçici mod uygulansın mı?",
                $"{device.Name} ({device.IpAddress}) için {modeText}. Süre: {durationText}."))
        {
            return;
        }

        IsBusy = true;
        try
        {
            var affected = await _deviceRepository.BulkSetSuppressionAsync(
                new[] { device.Id },
                mode,
                untilUtc,
                BulkSuppressionReason,
                Environment.UserName,
                DateTime.UtcNow);
            await ReloadAllAsync();
            StatusMessage = affected > 0
                ? $"{device.Name} için {mode.ToDisplayName()} uygulandı."
                : "Cihaz geçici modu güncellenemedi.";
        }
        finally
        {
            IsBusy = false;
        }
    }

    private async Task ClearDeviceSuppressionAsync(Device? device)
    {
        if (device is null)
        {
            return;
        }

        if (!_dialogService.Confirm(
                "Geçici mod kaldırılsın mı?",
                $"{device.Name} ({device.IpAddress}) normal izlemeye dönecek."))
        {
            return;
        }

        IsBusy = true;
        try
        {
            var affected = await _deviceRepository.BulkClearSuppressionAsync(new[] { device.Id }, DateTime.UtcNow);
            await ReloadAllAsync();
            StatusMessage = affected > 0
                ? $"{device.Name} normal izlemeye alındı."
                : "Cihaz geçici modu kaldırılamadı.";
        }
        finally
        {
            IsBusy = false;
        }
    }

    private async Task BulkSetAutoCheckAsync(object? parameter, bool enabled)
    {
        var devices = GetSelectedDevicesOrWarn(parameter, "Toplu işlem için cihaz seçin.");
        if (devices is null)
        {
            return;
        }

        if (!_dialogService.Confirm(
                "Otomatik kontrol güncellensin mi?",
                $"{devices.Count} cihaz için otomatik kontrol {(enabled ? "açılacak" : "kapatılacak")}."))
        {
            return;
        }

        await RunBulkOperationAsync(async () =>
        {
            var affected = await _deviceRepository.BulkSetAutoCheckAsync(devices.Select(device => device.Id), enabled);
            await ReloadAllAsync();
            StatusMessage = $"{affected} cihaz için otomatik kontrol {(enabled ? "açıldı" : "kapatıldı")}.";
        });
    }

    private async Task BulkAssignGroupAsync(object? parameter)
    {
        var devices = GetSelectedDevicesOrWarn(parameter, "Toplu işlem için cihaz seçin.");
        if (devices is null)
        {
            return;
        }

        var groupName = ResolveGroupName(BulkTargetGroupId);
        var targetText = string.IsNullOrWhiteSpace(groupName) ? "grupsuz yapılacak" : $"{groupName} grubuna atanacak";
        if (!_dialogService.Confirm("Seçili cihazlar gruba atansın mı?", $"{devices.Count} cihaz {targetText}."))
        {
            return;
        }

        await RunBulkOperationAsync(async () =>
        {
            var affected = await _deviceRepository.BulkSetGroupAsync(devices.Select(device => device.Id), BulkTargetGroupId, groupName);
            await ReloadAllAsync();
            StatusMessage = $"{affected} cihaz {(string.IsNullOrWhiteSpace(groupName) ? "grupsuz yapıldı" : $"{groupName} grubuna atandı")}.";
        });
    }

    private async Task BulkApplyCheckIntervalAsync(object? parameter)
    {
        var devices = GetSelectedDevicesOrWarn(parameter, "Toplu işlem için cihaz seçin.");
        if (devices is null)
        {
            return;
        }

        var intervalText = BulkCheckIntervalSeconds <= 0
            ? "özel kontrol aralığı kaldırılacak ve grup/tip/global politika kullanılacak"
            : $"kontrol aralığı {BulkCheckIntervalSeconds} sn yapılacak";
        if (!_dialogService.Confirm("Kontrol aralığı güncellensin mi?", $"{devices.Count} cihaz için {intervalText}."))
        {
            return;
        }

        await RunBulkOperationAsync(async () =>
        {
            var affected = await _deviceRepository.BulkSetCheckIntervalAsync(devices.Select(device => device.Id), BulkCheckIntervalSeconds);
            await ReloadAllAsync();
            StatusMessage = BulkCheckIntervalSeconds <= 0
                ? $"{affected} cihaz için özel kontrol aralığı kaldırıldı; grup/tip/global politika kullanılacak."
                : $"{affected} cihaz için kontrol aralığı {BulkCheckIntervalSeconds} sn olarak güncellendi.";
        });
    }

    private async Task BulkSetSuppressionAsync(object? parameter, DeviceSuppressionMode mode)
    {
        var devices = GetSelectedDevicesOrWarn(parameter, "Toplu işlem için cihaz seçin.");
        if (devices is null)
        {
            return;
        }

        var untilUtc = BulkSuppressionDurationHours <= 0
            ? (DateTime?)null
            : DateTime.UtcNow.AddHours(BulkSuppressionDurationHours);
        var modeText = mode == DeviceSuppressionMode.PauseMonitoring
            ? "izleme duraklatılacak"
            : "bildirimler susturulacak";
        var durationText = untilUtc.HasValue
            ? $"{BulkSuppressionDurationHours} saat"
            : "süresiz";
        if (!_dialogService.Confirm("Seçili cihazlarda geçici mod uygulansın mı?", $"{devices.Count} cihaz için {modeText}. Süre: {durationText}."))
        {
            return;
        }

        await RunBulkOperationAsync(async () =>
        {
            var affected = await _deviceRepository.BulkSetSuppressionAsync(
                devices.Select(device => device.Id),
                mode,
                untilUtc,
                BulkSuppressionReason,
                Environment.UserName,
                DateTime.UtcNow);
            await ReloadAllAsync();
            StatusMessage = $"{affected} cihaz için {mode.ToDisplayName()} uygulandı.";
        });
    }

    private async Task BulkClearSuppressionAsync(object? parameter)
    {
        var devices = GetSelectedDevicesOrWarn(parameter, "Toplu işlem için cihaz seçin.");
        if (devices is null)
        {
            return;
        }

        if (!_dialogService.Confirm("Geçici mod kaldırılsın mı?", $"{devices.Count} cihaz normal izlemeye dönecek."))
        {
            return;
        }

        await RunBulkOperationAsync(async () =>
        {
            var affected = await _deviceRepository.BulkClearSuppressionAsync(devices.Select(device => device.Id), DateTime.UtcNow);
            await ReloadAllAsync();
            StatusMessage = $"{affected} cihaz normal izlemeye alındı.";
        });
    }

    private async Task BulkSetActiveAsync(object? parameter, bool isActive)
    {
        var devices = GetSelectedDevicesOrWarn(parameter, "Toplu işlem için cihaz seçin.");
        if (devices is null)
        {
            return;
        }

        var selectedCount = devices.Count;
        var title = isActive ? "Seçili cihazlar aktif hale getirilsin mi?" : "Seçili cihazlar pasifleştirilsin mi?";
        var message = isActive
            ? $"{selectedCount} cihaz tekrar aktif listeye alınacak. Otomatik kontrol ayarı açık olanlar Worker tarafından kullanılabilir."
            : $"{selectedCount} cihaz otomatik kontrol ve manuel toplu işlemler dışında kalacak. Cihaz kayıtları silinmez.";

        if (!_dialogService.Confirm(title, message))
        {
            return;
        }

        await RunBulkOperationAsync(async () =>
        {
            var affected = await _deviceRepository.BulkSetActiveAsync(devices.Select(device => device.Id), isActive);
            await ReloadAllAsync();
            var summary = isActive
                ? $"{selectedCount} cihaz seçildi. {affected} cihaz aktif hale getirildi."
                : $"{selectedCount} cihaz seçildi. {affected} cihaz pasifleştirildi.";
            StatusMessage = summary;
            _dialogService.ShowInfo(isActive ? "Toplu aktif sonucu" : "Toplu pasif sonucu", summary);
        });
    }

    private async Task BulkDeleteAsync(object? parameter)
    {
        var devices = GetSelectedDevices(parameter).Where(device => !device.IsDeleted).ToList();
        if (devices.Count == 0)
        {
            _dialogService.ShowWarning("Cihaz seçilmedi", "Silinecek aktif cihaz seçin.");
            return;
        }

        var selectedCount = devices.Count;
        var names = string.Join(", ", devices.Take(5).Select(device => $"{device.Name} ({device.IpAddress})"));
        if (devices.Count > 5)
        {
            names += $" ve {devices.Count - 5} cihaz daha";
        }

        if (!_dialogService.Confirm(
                "Seçilen cihazlar silinsin mi?",
                $"{selectedCount} cihaz otomatik kontrollerden çıkarılacak.\n{names}\n\nGeçmiş ping ve kesinti kayıtları korunacaktır."))
        {
            return;
        }

        await RunBulkOperationAsync(async () =>
        {
            var result = await _deviceService.BulkDeleteAsync(devices);
            await ReloadAllAsync();
            var summary = $"{selectedCount} cihaz seçildi. {result.Message}";
            StatusMessage = summary;
            _dialogService.ShowInfo("Toplu silme sonucu", summary);
        });
    }

    private async Task BulkRestoreAsync(object? parameter)
    {
        var devices = GetSelectedDevices(parameter).Where(device => device.IsDeleted).ToList();
        if (devices.Count == 0)
        {
            _dialogService.ShowWarning("Cihaz secilmedi", "Geri yuklenecek silinmis cihaz secin.");
            return;
        }

        if (!_dialogService.Confirm("Secilen cihazlar geri yuklensin mi?", $"{devices.Count} cihaz tekrar aktif hale getirilecek. Duplicate aktif IP olusturulmaz."))
        {
            return;
        }

        await RunBulkOperationAsync(async () =>
        {
            var result = await _deviceService.BulkRestoreAsync(devices);
            await ReloadAllAsync();
            StatusMessage = result.Message;
        });
    }

    private bool CanUseSelectedDevices(object? parameter)
    {
        return !IsBusy && GetSelectedDevices(parameter).Count > 0;
    }

    private List<Device>? GetSelectedDevicesOrWarn(object? parameter, string warningMessage)
    {
        var devices = GetSelectedDevices(parameter);
        if (devices.Count > 0)
        {
            return devices;
        }

        _dialogService.ShowWarning("Cihaz seçilmedi", warningMessage);
        return null;
    }

    private async Task RunBulkOperationAsync(Func<Task> operation)
    {
        IsBusy = true;
        try
        {
            await operation();
        }
        finally
        {
            IsBusy = false;
        }
    }

    private void ToggleAllVisibleDevicesSelection()
    {
        foreach (var device in DevicesView.Cast<Device>())
        {
            device.IsSelected = _selectAllVisibleDevices;
        }

        OnPropertyChanged(nameof(SelectedDeviceCountText));
        OnPropertyChanged(nameof(PingSelectedDevicesText));
        RaiseCommandStates();
    }

    private List<Device> GetSelectedDevices(object? parameter)
    {
        var selected = new List<Device>();
        if (parameter is IEnumerable selectedItems and not string)
        {
            selected.AddRange(selectedItems
                .OfType<Device>()
                .Where(device => device.Id > 0)
                .DistinctBy(device => device.Id));
        }

        selected.AddRange(Devices.Where(device => device is { Id: > 0, IsSelected: true }));
        if (SelectedDevice is { Id: > 0 })
        {
            selected.Add(SelectedDevice);
        }

        return selected.DistinctBy(device => device.Id).ToList();
    }
}

