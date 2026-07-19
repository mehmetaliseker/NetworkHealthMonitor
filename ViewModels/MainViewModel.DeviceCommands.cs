using System.Windows;
using NetworkHealthMonitor.Models;
using NetworkHealthMonitor.Services;

namespace NetworkHealthMonitor.ViewModels;

public sealed partial class MainViewModel
{
    private async Task SaveDeviceAsync()
    {
        var groupName = ResolveGroupName(FormGroupId);
        var device = _editingDeviceId.HasValue
            ? Devices.FirstOrDefault(item => item.Id == _editingDeviceId.Value) ?? new Device { Id = _editingDeviceId.Value }
            : new Device();

        device.Name = FormName;
        device.IpAddress = FormIpAddress;
        device.DeviceType = FormDeviceType;
        device.GroupId = FormGroupId;
        device.GroupName = groupName;
        device.Location = FormLocation;
        device.Description = FormDescription;
        device.AutoCheckEnabled = FormAutoCheckEnabled;
        device.DefaultSchedulePlanId = FormDefaultSchedulePlanId;
        device.PingTimeoutMs = FormPingTimeoutMs;
        device.CheckIntervalSeconds = FormCheckIntervalSeconds;
        device.FailureRetryIntervalSeconds = FormFailureRetryIntervalSeconds;
        device.FailureRetryLimit = FormFailureRetryLimit;
        device.FailureThreshold = FormFailureThreshold;
        device.IsCritical = FormIsCritical;
        device.IsActive = FormIsActive;
        device.IsEnabled = FormIsActive && !device.IsDeleted;
        device.SlaTargetAvailabilityPercent = FormSlaTargetAvailabilityPercent;

        IsBusy = true;
        try
        {
            var result = await _deviceService.SaveAsync(device);
            if (!result.Success)
            {
                _dialogService.ShowWarning("Cihaz kaydedilemedi", result.Message);
                return;
            }

            StatusMessage = result.Message;
            await ReloadAllAsync();
            SelectedDevice = Devices.FirstOrDefault(item => item.Id == device.Id) ?? Devices.FirstOrDefault(item =>
                string.Equals(item.IpAddress, device.IpAddress, StringComparison.OrdinalIgnoreCase));
            IsDeviceFormVisible = SelectedDevice is not null && !IsCompactLayout;
            if (IsCompactLayout)
            {
                IsDeviceFormVisible = false;
            }

            CurrentSection = SectionDevices;
        }
        finally
        {
            IsBusy = false;
        }
    }

    private void StartEditDevice(Device? device)
    {
        if (device is null)
        {
            return;
        }

        _editingDeviceId = device.Id;
        FormName = device.Name;
        FormIpAddress = device.IpAddress;
        FormDeviceType = device.DeviceType;
        FormGroupId = device.GroupId;
        FormLocation = device.Location;
        FormDescription = device.Description;
        FormAutoCheckEnabled = device.AutoCheckEnabled;
        FormDefaultSchedulePlanId = device.DefaultSchedulePlanId;
        FormPingTimeoutMs = device.PingTimeoutMs;
        FormCheckIntervalSeconds = device.CheckIntervalSeconds;
        FormFailureRetryIntervalSeconds = device.FailureRetryIntervalSeconds;
        FormFailureRetryLimit = device.FailureRetryLimit;
        FormFailureThreshold = device.FailureThreshold;
        FormIsCritical = device.IsCritical;
        FormIsActive = device.IsActive;
        FormSlaTargetAvailabilityPercent = device.SlaTargetAvailabilityPercent;
        OnPropertyChanged(nameof(DeviceFormTitle));
        OnPropertyChanged(nameof(DeviceFormActionText));
        IsDeviceFormVisible = true;
        CurrentSection = SectionDevices;
    }

    private void ClearDeviceForm()
    {
        _editingDeviceId = null;
        FormName = string.Empty;
        FormIpAddress = string.Empty;
        FormDeviceType = DeviceType.Camera;
        FormGroupId = null;
        FormLocation = string.Empty;
        FormDescription = string.Empty;
        FormAutoCheckEnabled = true;
        FormDefaultSchedulePlanId = null;
        FormPingTimeoutMs = null;
        FormCheckIntervalSeconds = 0;
        FormFailureRetryIntervalSeconds = 0;
        FormFailureRetryLimit = 0;
        FormFailureThreshold = 0;
        FormIsCritical = false;
        FormIsActive = true;
        FormSlaTargetAvailabilityPercent = null;
        OnPropertyChanged(nameof(DeviceFormTitle));
        OnPropertyChanged(nameof(DeviceFormActionText));
    }

    private void ClearDeviceFilters()
    {
        DeviceSearchText = string.Empty;
        DeviceTypeFilter = AllDeviceTypesText;
        DeviceStatusFilter = AllStatusesText;
        DeviceGroupFilter = AllGroupsText;
        CriticalFilter = AllCriticalText;
        DeletedDeviceFilter = ActiveDevicesText;
        AutoCheckFilter = AllAutoCheckStatesText;
        SuppressionFilter = AllSuppressionStatesText;
        UptimeRangeFilter = AllUptimeRangesText;
        LastCheckRangeFilter = AllLastCheckRangesText;
        DeviceOnlyProblematic = false;
        OnPropertyChanged(nameof(ActiveDeviceFilterCount));
        OnPropertyChanged(nameof(ActiveDeviceFilterCountText));
        OnPropertyChanged(nameof(ActiveDeviceFilterSummaryText));
        OnPropertyChanged(nameof(HasNoFilteredDevices));
    }

    private async Task DeleteDeviceAsync(Device? device)
    {
        if (device is null)
        {
            return;
        }

        var activePlanCount = SchedulePlans.Count(plan =>
            plan.IsActive
            && _schedulePlanTargetResolver.ResolveTargets(plan, new[] { device }, DeviceGroups, respectAutoCheck: false).Any());
        var message = $"""
            “{device.Name}” adlı cihaz aktif listeden kaldırılacaktır.

            IP adresi: {device.IpAddress}
            Cihaz türü: {device.DeviceType.ToDisplayName()}
            Grup: {(string.IsNullOrWhiteSpace(device.GroupName) ? "-" : device.GroupName)}
            Etkilenecek aktif plan sayısı: {activePlanCount}

            Geçmiş ping kayıtları ve kesinti geçmişi korunacaktır.
            """;

        if (!_dialogService.Confirm("Cihaz silinsin mi?", message))
        {
            return;
        }

        IsBusy = true;
        try
        {
            var result = await _deviceService.DeleteAsync(device);
            StatusMessage = result.Success ? "Cihaz silindi." : result.Message;
            if (SelectedDevice?.Id == device.Id)
            {
                SelectedDevice = null;
            }

            IsDeviceFormVisible = false;
            ClearDeviceForm();
            await ReloadAllAsync();
        }
        finally
        {
            IsBusy = false;
        }
    }

    private async Task RestoreDeviceAsync(Device? device)
    {
        if (device is null)
        {
            return;
        }

        if (!_dialogService.Confirm("Cihaz geri yuklensin mi?", $"{device.Name} tekrar aktif hale getirilecek. Otomatik kontrol tekrar acilir."))
        {
            return;
        }

        IsBusy = true;
        try
        {
            var result = await _deviceService.RestoreAsync(device);
            StatusMessage = result.Message;
            await ReloadAllAsync();
        }
        finally
        {
            IsBusy = false;
        }
    }

    private async Task SaveGroupAsync()
    {
        var group = _editingGroupId.HasValue
            ? DeviceGroups.FirstOrDefault(item => item.Id == _editingGroupId.Value) ?? new DeviceGroup { Id = _editingGroupId.Value }
            : new DeviceGroup();

        group.Name = GroupFormName;
        group.Description = GroupFormDescription;
        group.DefaultSchedulePlanId = GroupFormDefaultSchedulePlanId;
        group.DefaultAutoCheckEnabled = GroupFormDefaultAutoCheckEnabled;
        group.DefaultCheckIntervalSeconds = GroupFormDefaultCheckIntervalSeconds;
        group.DefaultPingTimeoutMs = GroupFormDefaultPingTimeoutMs;
        group.DefaultFailureRetryIntervalSeconds = GroupFormDefaultFailureRetryIntervalSeconds;
        group.DefaultFailureRetryLimit = GroupFormDefaultFailureRetryLimit;
        group.DefaultFailureThreshold = GroupFormDefaultFailureThreshold;
        group.TargetAvailabilityPercent = GroupFormTargetAvailabilityPercent;

        IsBusy = true;
        try
        {
            var result = await _deviceGroupService.SaveAsync(group);
            if (!result.Success)
            {
                _dialogService.ShowWarning("Grup kaydedilemedi", result.Message);
                return;
            }

            StatusMessage = result.Message;
            ClearGroupForm();
            await ReloadAllAsync();
        }
        finally
        {
            IsBusy = false;
        }
    }

    private void StartEditGroup(DeviceGroup? group)
    {
        if (group is null)
        {
            return;
        }

        _editingGroupId = group.Id;
        GroupFormName = group.Name;
        GroupFormDescription = group.Description;
        GroupFormDefaultSchedulePlanId = group.DefaultSchedulePlanId;
        GroupFormDefaultAutoCheckEnabled = group.DefaultAutoCheckEnabled;
        GroupFormDefaultCheckIntervalSeconds = group.DefaultCheckIntervalSeconds;
        GroupFormDefaultPingTimeoutMs = group.DefaultPingTimeoutMs;
        GroupFormDefaultFailureRetryIntervalSeconds = group.DefaultFailureRetryIntervalSeconds;
        GroupFormDefaultFailureRetryLimit = group.DefaultFailureRetryLimit;
        GroupFormDefaultFailureThreshold = group.DefaultFailureThreshold;
        GroupFormTargetAvailabilityPercent = group.TargetAvailabilityPercent;
        OnPropertyChanged(nameof(GroupFormTitle));
        OnPropertyChanged(nameof(GroupFormActionText));
    }

    private void ClearGroupForm()
    {
        _editingGroupId = null;
        GroupFormName = string.Empty;
        GroupFormDescription = string.Empty;
        GroupFormDefaultSchedulePlanId = null;
        GroupFormDefaultAutoCheckEnabled = null;
        GroupFormDefaultCheckIntervalSeconds = null;
        GroupFormDefaultPingTimeoutMs = null;
        GroupFormDefaultFailureRetryIntervalSeconds = null;
        GroupFormDefaultFailureRetryLimit = null;
        GroupFormDefaultFailureThreshold = null;
        GroupFormTargetAvailabilityPercent = null;
        OnPropertyChanged(nameof(GroupFormTitle));
        OnPropertyChanged(nameof(GroupFormActionText));
    }

    private async Task DeleteGroupAsync(DeviceGroup? group)
    {
        if (group is null)
        {
            return;
        }

        if (!_dialogService.Confirm("Grup silinsin mi?", $"{group.Name} grubu silinecek. Cihazlar silinmez, sadece grup bağlantısı kaldırılır."))
        {
            return;
        }

        IsBusy = true;
        try
        {
            var result = await _deviceGroupService.DeleteAsync(group);
            StatusMessage = result.Message;
            await ReloadAllAsync();
        }
        finally
        {
            IsBusy = false;
        }
    }

    private async Task DeleteGroupDevicesAsync(DeviceGroup? group)
    {
        if (group is null)
        {
            return;
        }

        var devices = Devices.Where(device => !device.IsDeleted && device.GroupId == group.Id).ToList();
        var activePlanCount = SchedulePlans.Count(plan =>
            plan.IsActive
            && _schedulePlanTargetResolver.ResolveTargets(plan, devices, DeviceGroups, respectAutoCheck: false).Any());

        if (devices.Count == 0)
        {
            _dialogService.ShowWarning("Cihaz bulunamadi", "Bu grupta silinecek aktif cihaz yok.");
            return;
        }

        var message = $"""
            Grup: {group.Name}
            Bu grupta {devices.Count} aktif cihaz bulunmaktadir.
            Aktif plan sayisi: {activePlanCount}

            {devices.Count} cihaz otomatik kontrollerden cikarilacaktir.
            Gecmis ping ve kesinti kayitlari korunacaktir.
            {(DeleteEmptyGroupAfterDeviceDelete ? "Cihazlar silindikten sonra bos grup da silinecektir." : "Grup kaydi korunacaktir.")}
            """;

        if (!_dialogService.Confirm("Bu gruptaki tum cihazlar silinsin mi?", message))
        {
            return;
        }

        IsBusy = true;
        try
        {
            var result = await _deviceService.DeleteGroupDevicesAsync(group, DeleteEmptyGroupAfterDeviceDelete);
            StatusMessage = result.Message;
            await ReloadAllAsync();
        }
        finally
        {
            IsBusy = false;
        }
    }

    private async Task PingGroupAsync(DeviceGroup group)
    {
        var devices = Devices.Where(device => device.GroupId == group.Id).ToList();
        if (devices.Count == 0)
        {
            _dialogService.ShowWarning("Cihaz bulunamadı", "Bu gruba atanmış cihaz bulunmuyor.");
            return;
        }

        await RunManualPingAsync(devices, PingTriggerType.GroupManual);
    }

    private async Task PingSelectedTypeAsync()
    {
        var type = ParseDeviceTypeFilter(DeviceTypeFilter);
        if (!type.HasValue)
        {
            return;
        }

        await RunManualPingAsync(Devices.Where(device => device.DeviceType == type.Value).ToList(), PingTriggerType.TypeManual);
    }

    private async Task RunManualPingAsync(
        IEnumerable<Device> devices,
        PingTriggerType triggerType,
        SchedulePlan? schedulePlan = null)
    {
        var targets = devices.Where(device => device.IsActive && device.IsEnabled && !device.IsDeleted).DistinctBy(device => device.Id).ToList();
        if (targets.Count == 0)
        {
            _dialogService.ShowWarning("Cihaz bulunamadı", "Kontrol edilecek aktif cihaz bulunamadı.");
            return;
        }

        _pingCancellationTokenSource?.Cancel();
        _pingCancellationTokenSource?.Dispose();
        _pingCancellationTokenSource = new CancellationTokenSource();
        ResetPingProgress(targets.Count);
        IsPinging = true;
        IsBusy = true;
        StatusMessage = $"{targets.Count} cihaz kontrol ediliyor...";

        var progress = new Progress<PingProgress>(ApplyPingProgress);
        try
        {
            var options = schedulePlan?.ToPingOptions() ?? new PingOptions(PingTimeoutMs, MaxParallelPings, DefaultFailureThreshold);
            var result = await _pingExecutionService.PingDevicesAsync(
                targets,
                options,
                triggerType,
                schedulePlan,
                progress,
                _pingCancellationTokenSource.Token);

            StatusMessage = $"Ping tamamlandı. Başarılı: {result.SuccessCount}, başarısız: {result.FailureCount}, atlanan: {result.SkippedBecauseAlreadyRunning}.";
            await ReloadAllAsync();
        }
        catch (OperationCanceledException)
        {
            StatusMessage = "Ping işlemi iptal edildi.";
        }
        finally
        {
            IsPinging = false;
            IsBusy = false;
        }
    }

    private void CancelPing()
    {
        _pingCancellationTokenSource?.Cancel();
    }

    private void ApplyPingProgress(PingProgress progress)
    {
        PingTotalCount = progress.Total;
        PingCompletedCount = progress.Completed;
        PingSuccessCount = progress.Success;
        PingFailureCount = progress.Failure;

        if (progress.DeviceId.HasValue && progress.DeviceStatus.HasValue)
        {
            var device = Devices.FirstOrDefault(item => item.Id == progress.DeviceId.Value);
            if (device is not null)
            {
                device.LastStatus = progress.DeviceStatus.Value;
                if (progress.LatencyMs.HasValue || progress.DeviceStatus.Value != DeviceStatus.Checking)
                {
                    device.LastLatencyMs = progress.LatencyMs;
                }

                if (progress.CheckedAt.HasValue)
                {
                    device.LastCheckedAt = progress.CheckedAt;
                }
            }
        }

        StatusMessage = $"Kontrol ediliyor: {progress.Completed}/{progress.Total} tamamlandı.";
    }

    private void ResetPingProgress(int total)
    {
        PingTotalCount = total;
        PingCompletedCount = 0;
        PingSuccessCount = 0;
        PingFailureCount = 0;
    }
}

