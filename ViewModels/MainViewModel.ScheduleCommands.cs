using System.Globalization;
using System.Windows;
using NetworkHealthMonitor.Models;
using NetworkHealthMonitor.Services;
using WpfApplication = System.Windows.Application;

namespace NetworkHealthMonitor.ViewModels;

public sealed partial class MainViewModel
{
    private async Task SaveSchedulePlanAsync()
    {
        var plan = _editingPlanId.HasValue
            ? SchedulePlans.FirstOrDefault(item => item.Id == _editingPlanId.Value) ?? new SchedulePlan { Id = _editingPlanId.Value }
            : new SchedulePlan();

        plan.Name = PlanFormName;
        plan.TargetType = PlanFormTargetType;
        plan.TargetValue = PlanFormTargetValue;
        plan.IntervalMinutes = ConvertFrequencyToMinutes(PlanFormFrequencyValue, PlanFormFrequencyUnit);
        plan.TimeoutMs = PlanFormTimeoutMs;
        plan.MaxParallelism = PlanFormMaxParallelism;
        plan.FailureThreshold = PlanFormFailureThreshold;
        plan.IsActive = PlanFormIsActive;
        plan.Description = PlanFormDescription;

        IsBusy = true;
        try
        {
            var result = await _schedulePlanService.SaveAsync(plan);
            if (!result.Success)
            {
                _dialogService.ShowWarning("Plan kaydedilemedi", result.Message);
                return;
            }

            StatusMessage = result.Message;
            ClearSchedulePlanForm();
            await ReloadAllAsync();
        }
        finally
        {
            IsBusy = false;
        }
    }

    private void StartEditSchedulePlan(SchedulePlan? plan)
    {
        if (plan is null)
        {
            return;
        }

        _editingPlanId = plan.Id;
        PlanFormName = plan.Name;
        PlanFormTargetType = plan.TargetType;
        UpdatePlanTargetOptions();
        PlanFormTargetValue = plan.TargetValue;
        ApplyFrequency(plan.IntervalMinutes);
        PlanFormTimeoutMs = plan.TimeoutMs;
        PlanFormMaxParallelism = plan.MaxParallelism;
        PlanFormFailureThreshold = plan.FailureThreshold;
        PlanFormIsActive = plan.IsActive;
        PlanFormDescription = plan.Description;
        OnPropertyChanged(nameof(PlanFormTitle));
        OnPropertyChanged(nameof(PlanFormActionText));
    }

    private void ClearSchedulePlanForm()
    {
        _editingPlanId = null;
        PlanFormName = string.Empty;
        PlanFormTargetType = SchedulePlanTargetType.AllDevices;
        PlanFormTargetValue = string.Empty;
        PlanFormFrequencyValue = AppSettings.DefaultSchedulePlanIntervalMinutes;
        PlanFormFrequencyUnit = "Dakika";
        PlanFormTimeoutMs = PingTimeoutMs;
        PlanFormMaxParallelism = Math.Min(MaxParallelPings, AppSettings.DefaultSchedulePlanMaxParallelism);
        PlanFormFailureThreshold = DefaultFailureThreshold;
        PlanFormIsActive = true;
        PlanFormDescription = string.Empty;
        UpdatePlanTargetOptions();
        OnPropertyChanged(nameof(PlanFormTitle));
        OnPropertyChanged(nameof(PlanFormActionText));
    }

    private async Task DeleteSchedulePlanAsync(SchedulePlan? plan)
    {
        if (plan is null)
        {
            return;
        }

        if (!_dialogService.Confirm("Plan silinsin mi?", $"{plan.Name} otomatik kontrol planı silinecek."))
        {
            return;
        }

        IsBusy = true;
        try
        {
            var result = await _schedulePlanService.DeleteAsync(plan);
            StatusMessage = result.Message;
            await ReloadAllAsync();
        }
        finally
        {
            IsBusy = false;
        }
    }

    private async Task RunSchedulePlanNowAsync(SchedulePlan plan)
    {
        if (!plan.IsActive)
        {
            _dialogService.ShowWarning("Plan pasif", "Pasif otomatik kontrol planı çalıştırılamaz.");
            return;
        }

        var targets = _schedulePlanTargetResolver.ResolveTargets(plan, Devices, DeviceGroups, respectAutoCheck: false);
        if (targets.Count == 0)
        {
            _dialogService.ShowWarning("Hedef bulunamadı", "Bu plan için kontrol edilecek aktif cihaz bulunamadı.");
            return;
        }

        await RunManualPingAsync(targets, PingTriggerType.Manual, plan);
        await LoadSchedulePlansAsync();
    }

    private async Task StartSchedulerAsync()
    {
        await ShowServiceControlInfoAsync();
    }

    private async Task StopSchedulerAsync()
    {
        await ShowServiceControlInfoAsync();
    }

    private async Task ShowServiceControlInfoAsync()
    {
        await RefreshWorkerServiceStatusAsync();
        _dialogService.ShowInfo(
            "Windows Service yönetimi",
            "Otomatik izleme Windows Service tarafından çalıştırılır. Başlatma, durdurma ve kurulum işlemleri için yönetici PowerShell ile scripts klasöründeki servis scriptlerini kullanın. Arayüz kapanırsa izleme servisi çalışmaya devam eder.");
    }

    private async Task RefreshWorkerServiceStatusAsync()
    {
        var status = await _windowsServiceStatusService.GetStatusAsync();
        SchedulerStatusText = status.DisplayText;
        IsSchedulerRunning = status.Code == "Running";
    }

    public async Task<string> GetWorkerServiceStatusTextAsync()
    {
        await RefreshWorkerServiceStatusAsync();
        return SchedulerStatusText;
    }

    private void SchedulerStatusChanged(object? sender, SchedulerStatusChangedEventArgs e)
    {
        var dispatcher = WpfApplication.Current?.Dispatcher;
        if (dispatcher is null || dispatcher.CheckAccess())
        {
            StatusMessage = e.Message;
            IsSchedulerRunning = _schedulerService.IsRunning;
            if (e.ShouldRefresh)
            {
                _ = RefreshAfterSchedulerAsync();
            }

            return;
        }

        dispatcher.BeginInvoke(() =>
        {
            StatusMessage = e.Message;
            IsSchedulerRunning = _schedulerService.IsRunning;
            if (e.ShouldRefresh)
            {
                _ = RefreshAfterSchedulerAsync();
            }
        });
    }

    private async Task RefreshAfterSchedulerAsync()
    {
        if (IsBusy || IsPinging)
        {
            return;
        }

        await LoadDevicesAsync();
        await LoadAvailabilityAsync();
        await LoadOpenOutagesAsync();
        await LoadLogsAsync();
    }

    private void UpdatePlanTargetOptions(bool keepCurrentValue = false)
    {
        var current = keepCurrentValue ? PlanFormTargetValue : string.Empty;
        var options = PlanFormTargetType switch
        {
            SchedulePlanTargetType.Device => Devices.Select(device => new SelectionOption<string>(device.Id.ToString(CultureInfo.InvariantCulture), $"{device.IpAddress} - {device.Name}")),
            SchedulePlanTargetType.DeviceType => DeviceTypeOptions.Select(option => new SelectionOption<string>(option.Value.ToStorageValue(), option.Label)),
            SchedulePlanTargetType.DeviceGroup => DeviceGroups.Select(group => new SelectionOption<string>(group.Id.ToString(CultureInfo.InvariantCulture), group.Name)),
            SchedulePlanTargetType.CriticalDevices => new[] { new SelectionOption<string>(string.Empty, "Kritik cihazlar") },
            SchedulePlanTargetType.AllDevices => new[] { new SelectionOption<string>(string.Empty, "Tüm cihazlar") },
            _ => new[] { new SelectionOption<string>(string.Empty, "Tüm cihazlar") }
        };

        ReplaceCollection(PlanTargetOptions, options);
        PlanFormTargetValue = PlanTargetOptions.Any(option => option.Value == current)
            ? current
            : PlanTargetOptions.FirstOrDefault()?.Value ?? string.Empty;
    }

    private string ResolvePlanTargetDisplayName(SchedulePlan plan)
    {
        return plan.TargetType switch
        {
            SchedulePlanTargetType.Device => Devices.FirstOrDefault(device => device.Id.ToString(CultureInfo.InvariantCulture) == plan.TargetValue) is { } device
                ? $"{device.IpAddress} - {device.Name}"
                : plan.TargetValue,
            SchedulePlanTargetType.DeviceType => DeviceTypeExtensions.FromStorageValue(plan.TargetValue).ToDisplayName(),
            SchedulePlanTargetType.DeviceGroup => DeviceGroups.FirstOrDefault(group => group.Id.ToString(CultureInfo.InvariantCulture) == plan.TargetValue)?.Name ?? plan.TargetValue,
            SchedulePlanTargetType.CriticalDevices => "Kritik cihazlar",
            SchedulePlanTargetType.AllDevices => "Tüm cihazlar",
            _ => plan.TargetValue
        };
    }

    private static int ConvertFrequencyToMinutes(int value, string unit)
    {
        var normalized = Math.Max(1, value);
        return unit switch
        {
            "Saat" => normalized * 60,
            "Gün" => normalized * 24 * 60,
            _ => normalized
        };
    }

    private void ApplyFrequency(int intervalMinutes)
    {
        if (intervalMinutes % (24 * 60) == 0)
        {
            PlanFormFrequencyValue = Math.Max(1, intervalMinutes / (24 * 60));
            PlanFormFrequencyUnit = "Gün";
            return;
        }

        if (intervalMinutes % 60 == 0)
        {
            PlanFormFrequencyValue = Math.Max(1, intervalMinutes / 60);
            PlanFormFrequencyUnit = "Saat";
            return;
        }

        PlanFormFrequencyValue = Math.Max(1, intervalMinutes);
        PlanFormFrequencyUnit = "Dakika";
    }
}


