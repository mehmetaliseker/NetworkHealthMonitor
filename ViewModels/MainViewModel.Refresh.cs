using System.Collections.ObjectModel;
using NetworkHealthMonitor.Models;
using NetworkHealthMonitor.Services;

namespace NetworkHealthMonitor.ViewModels;

public sealed partial class MainViewModel
{
    public async Task InitializeAsync()
    {
        if (_isInitialized)
        {
            return;
        }

        _isInitialized = true;
        IsBusy = true;
        try
        {
            var settings = await _settingsService.LoadAsync();
            if (_settingsService.LastLoadUsedDefaultsDueToError)
            {
                _dialogService.ShowWarning("Ayar dosyası okunamadı", "Ayar dosyası okunamadı; varsayılan ayarlar yüklendi.");
            }

            ApplySettings(settings);
            await RunStartupRetentionCleanupAsync(settings);
            await ReloadAllAsync();
            if (StartSchedulePlansOnStartup)
            {
                await StartSchedulerAsync();
            }

            StatusMessage = "Hazır.";
        }
        finally
        {
            IsBusy = false;
        }
    }

    public async ValueTask DisposeAsync()
    {
        _pingCancellationTokenSource?.Cancel();
        _pingCancellationTokenSource?.Dispose();
        await _schedulerService.StopAsync();
        _schedulerService.StatusChanged -= SchedulerStatusChanged;
    }

    private async Task ReloadAllAsync()
    {
        await LoadDevicesAsync();
        await LoadGroupsAsync();
        await LoadSchedulePlansAsync();
        await LoadAvailabilityAsync();
        await LoadOpenOutagesAsync();
        await LoadLogsAsync();
        UpdateDashboard();
        RaiseCommandStates();
    }

    private async Task LoadDevicesAsync()
    {
        var devices = await _deviceRepository.GetAllAsync();
        var metrics = await _pingLogRepository.GetDeviceHealthMetricsAsync(DateTime.Now.AddDays(-30));
        var settings = await _settingsService.LoadAsync();
        var groups = await _deviceGroupRepository.GetAllAsync();
        var groupsById = groups.ToDictionary(group => group.Id);
        var groupsByName = groups.ToDictionary(group => group.Name, StringComparer.OrdinalIgnoreCase);
        foreach (var device in devices)
        {
            var group = ResolveGroup(device, groupsById, groupsByName);
            device.EffectivePolicyText = _deviceCheckPolicyService.ResolvePolicy(
                device,
                group,
                null,
                settings,
                new PingOptions(settings.PingTimeoutMs, settings.MaxParallelPings, settings.DefaultFailureThreshold)).PolicySourceText;

            if (!metrics.TryGetValue(device.Id, out var metric))
            {
                continue;
            }

            device.Uptime24HoursPercent = metric.Uptime24HoursPercent;
            device.Uptime7DaysPercent = metric.Uptime7DaysPercent;
            device.Uptime30DaysPercent = metric.Uptime30DaysPercent;
            device.UptimeOverallPercent = metric.UptimeOverallPercent;
            device.AverageLatencyMs = metric.AverageLatencyMs;
            device.LastFailureAt = metric.LastFailureAt;
        }

        ReplaceCollection(Devices, devices);
        DevicesView.Refresh();
    }

    private async Task LoadGroupsAsync()
    {
        var groups = await _deviceGroupRepository.GetAllAsync();
        var availabilityByGroup = AvailabilityItems
            .Where(item => !string.IsNullOrWhiteSpace(item.GroupName) && item.Availability30DaysPercent.HasValue)
            .GroupBy(item => item.GroupName)
            .ToDictionary(
                group => group.Key,
                group => (double?)group.Average(item => item.Availability30DaysPercent!.Value),
                StringComparer.OrdinalIgnoreCase);

        foreach (var group in groups)
        {
            if (availabilityByGroup.TryGetValue(group.Name, out var availability))
            {
                group.Availability30DaysPercent = availability;
            }
        }

        ReplaceCollection(DeviceGroups, groups);
        RefreshGroupOptions();
        UpdatePlanTargetOptions(keepCurrentValue: true);
    }

    private async Task LoadSchedulePlansAsync()
    {
        var plans = await _schedulePlanRepository.GetAllAsync();
        foreach (var plan in plans)
        {
            plan.TargetDisplayName = ResolvePlanTargetDisplayName(plan);
        }

        ReplaceCollection(SchedulePlans, plans);
        RefreshSchedulePlanOptions();
        UpdatePlanTargetOptions(keepCurrentValue: true);
    }

    private async Task LoadAvailabilityAsync()
    {
        var items = await _availabilityService.GetDeviceAvailabilityAsync(DateTime.Now.AddDays(-30));
        ReplaceCollection(AvailabilityItems, items);
        ApplyGroupAvailabilityToGroups();
        UpdateGroupAvailabilityRows();
        UpdateDashboard();
    }

    private async Task LoadOpenOutagesAsync()
    {
        ReplaceCollection(OpenOutages, await _outageRepository.GetOpenAsync());
    }

    private async Task LoadLogsAsync()
    {
        var logs = await _pingLogRepository.GetFilteredAsync(
            LogStartDate,
            LogEndDate,
            LogDeviceNameFilter,
            LogIpAddressFilter,
            ParseDeviceTypeFilter(LogDeviceTypeFilter),
            ParseStatusFilter(LogStatusFilter),
            LogGroupFilter == AllGroupsText ? null : LogGroupFilter,
            ParseTriggerFilter(LogTriggerFilter),
            LogPlanNameFilter,
            LogOnlyUnreachable,
            5000);

        ReplaceCollection(Logs, logs);
        LogsView.Refresh();
        UpdateDashboard();
        RaiseCommandStates();
    }

    private bool FilterDevice(object item)
    {
        if (item is not Device device)
        {
            return false;
        }

        if (!string.IsNullOrWhiteSpace(DeviceSearchText))
        {
            var search = DeviceSearchText.Trim();
            if (!Contains(device.Name, search)
                && !Contains(device.IpAddress, search)
                && !Contains(device.Location, search)
                && !Contains(device.GroupName, search)
                && !Contains(device.Description, search))
            {
                return false;
            }
        }

        var type = ParseDeviceTypeFilter(DeviceTypeFilter);
        if (type.HasValue && device.DeviceType != type.Value)
        {
            return false;
        }

        var status = ParseStatusFilter(DeviceStatusFilter);
        if (status.HasValue && device.LastStatus != status.Value)
        {
            return false;
        }

        if (DeviceGroupFilter != AllGroupsText && !string.Equals(device.GroupName, DeviceGroupFilter, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        if (CriticalFilter == CriticalOnlyText && !device.IsCritical)
        {
            return false;
        }

        if (CriticalFilter == NonCriticalOnlyText && device.IsCritical)
        {
            return false;
        }

        return true;
    }

    private void RefreshGroupOptions()
    {
        ReplaceCollection(DeviceGroupOptions, new[] { new SelectionOption<int?>(null, "Grupsuz") }
            .Concat(DeviceGroups.Select(group => new SelectionOption<int?>((int?)group.Id, group.Name))));

        DeviceGroupFilterOptions.Clear();
        DeviceGroupFilterOptions.Add(AllGroupsText);
        foreach (var groupName in DeviceGroups.Select(group => group.Name).OrderBy(name => name))
        {
            DeviceGroupFilterOptions.Add(groupName);
        }

        if (!DeviceGroupFilterOptions.Contains(DeviceGroupFilter))
        {
            DeviceGroupFilter = AllGroupsText;
        }

        if (!DeviceGroupFilterOptions.Contains(LogGroupFilter))
        {
            LogGroupFilter = AllGroupsText;
        }

        if (BulkTargetGroupId.HasValue && DeviceGroups.All(group => group.Id != BulkTargetGroupId.Value))
        {
            BulkTargetGroupId = null;
        }
    }

    private void RefreshSchedulePlanOptions()
    {
        ReplaceCollection(SchedulePlanOptions, new[] { new SelectionOption<int?>(null, "Plan yok") }
            .Concat(SchedulePlans.Select(plan => new SelectionOption<int?>((int?)plan.Id, plan.Name))));
    }

    private string ResolveGroupName(int? groupId)
    {
        return groupId.HasValue
            ? DeviceGroups.FirstOrDefault(group => group.Id == groupId.Value)?.Name ?? string.Empty
            : string.Empty;
    }

    private static DeviceGroup? ResolveGroup(
        Device device,
        IReadOnlyDictionary<int, DeviceGroup> groupsById,
        IReadOnlyDictionary<string, DeviceGroup> groupsByName)
    {
        if (device.GroupId.HasValue && groupsById.TryGetValue(device.GroupId.Value, out var groupById))
        {
            return groupById;
        }

        return !string.IsNullOrWhiteSpace(device.GroupName) && groupsByName.TryGetValue(device.GroupName, out var groupByName)
            ? groupByName
            : null;
    }

    private DeviceType? ParseDeviceTypeFilter(string value)
    {
        if (value == AllDeviceTypesText)
        {
            return null;
        }

        var option = DeviceTypeOptions.FirstOrDefault(item => item.Label == value);
        return option?.Value;
    }

    private static DeviceStatus? ParseStatusFilter(string value)
    {
        if (value == AllStatusesText)
        {
            return null;
        }

        var statuses = new[]
        {
            DeviceStatus.Online,
            DeviceStatus.Warning,
            DeviceStatus.UnderWatch,
            DeviceStatus.Offline,
            DeviceStatus.PingBlockedOrNoReply,
            DeviceStatus.Unknown
        };

        foreach (var status in statuses)
        {
            if (status.ToDisplayName() == value)
            {
                return status;
            }
        }

        return null;
    }

    private static PingTriggerType? ParseTriggerFilter(string value)
    {
        if (value == AllTriggersText)
        {
            return null;
        }

        return Enum.GetValues<PingTriggerType>().FirstOrDefault(trigger => trigger.ToDisplayName() == value);
    }

    private static bool Contains(string source, string search)
    {
        return source?.IndexOf(search, StringComparison.CurrentCultureIgnoreCase) >= 0;
    }

    private static void ReplaceCollection<T>(ObservableCollection<T> collection, IEnumerable<T> items)
    {
        collection.Clear();
        foreach (var item in items)
        {
            collection.Add(item);
        }
    }

    private void RaiseCommandStates()
    {
        SaveDeviceCommand?.NotifyCanExecuteChanged();
        ClearDeviceFormCommand?.NotifyCanExecuteChanged();
        EditSelectedDeviceCommand?.NotifyCanExecuteChanged();
        DeleteSelectedDeviceCommand?.NotifyCanExecuteChanged();
        PingAllCommand?.NotifyCanExecuteChanged();
        PingFilteredDevicesCommand?.NotifyCanExecuteChanged();
        PingSelectedDeviceCommand?.NotifyCanExecuteChanged();
        PingSelectedTypeCommand?.NotifyCanExecuteChanged();
        PingSelectedDevicesBulkCommand?.NotifyCanExecuteChanged();
        EnableAutoCheckSelectedCommand?.NotifyCanExecuteChanged();
        DisableAutoCheckSelectedCommand?.NotifyCanExecuteChanged();
        AssignSelectedDevicesToGroupCommand?.NotifyCanExecuteChanged();
        ApplySelectedCheckIntervalCommand?.NotifyCanExecuteChanged();
        DeactivateSelectedDevicesCommand?.NotifyCanExecuteChanged();
        CancelPingCommand?.NotifyCanExecuteChanged();
        SaveGroupCommand?.NotifyCanExecuteChanged();
        ClearGroupFormCommand?.NotifyCanExecuteChanged();
        EditSelectedGroupCommand?.NotifyCanExecuteChanged();
        DeleteSelectedGroupCommand?.NotifyCanExecuteChanged();
        PingSelectedGroupCommand?.NotifyCanExecuteChanged();
        SaveSchedulePlanCommand?.NotifyCanExecuteChanged();
        ClearSchedulePlanFormCommand?.NotifyCanExecuteChanged();
        EditSelectedSchedulePlanCommand?.NotifyCanExecuteChanged();
        DeleteSelectedSchedulePlanCommand?.NotifyCanExecuteChanged();
        RunSelectedSchedulePlanCommand?.NotifyCanExecuteChanged();
        StartSchedulerCommand?.NotifyCanExecuteChanged();
        StopSchedulerCommand?.NotifyCanExecuteChanged();
        RefreshLogsCommand?.NotifyCanExecuteChanged();
        ClearLogsCommand?.NotifyCanExecuteChanged();
        ClearOldLogsCommand?.NotifyCanExecuteChanged();
        ExportLogsCommand?.NotifyCanExecuteChanged();
        RefreshAvailabilityCommand?.NotifyCanExecuteChanged();
        ExportAvailabilityCommand?.NotifyCanExecuteChanged();
        ExportDevicesCommand?.NotifyCanExecuteChanged();
        ImportDevicesCommand?.NotifyCanExecuteChanged();
        CreateCsvTemplateCommand?.NotifyCanExecuteChanged();
        SaveSettingsCommand?.NotifyCanExecuteChanged();
        ResetSettingsCommand?.NotifyCanExecuteChanged();
        BackupDatabaseCommand?.NotifyCanExecuteChanged();
        RestoreDatabaseCommand?.NotifyCanExecuteChanged();
        OptimizeDatabaseCommand?.NotifyCanExecuteChanged();
        ExportSettingsCommand?.NotifyCanExecuteChanged();
        ImportSettingsCommand?.NotifyCanExecuteChanged();
    }
}

