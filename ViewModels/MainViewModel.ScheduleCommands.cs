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
        plan.ScheduleMode = PlanFormScheduleMode;
        plan.IntervalValue = PlanFormFrequencyValue;
        plan.IntervalUnit = ConvertFrequencyUnit(PlanFormFrequencyUnit);
        plan.IntervalMinutes = ConvertFrequencyToMinutes(PlanFormFrequencyValue, PlanFormFrequencyUnit);
        plan.TimesPerDay = PlanFormTimesPerDay;
        plan.DailyTimes = PlanFormDailyTimes;
        plan.SelectedWeekDays = PlanFormSelectedWeekDays;
        plan.TimeZoneId = TimeZoneInfo.Local.Id;
        plan.FailureRetryEnabled = PlanFormFailureRetryEnabled;
        plan.ConfirmationRetryCount = PlanFormConfirmationRetryCount;
        plan.ConfirmationRetryIntervalSeconds = PlanFormConfirmationRetryIntervalSeconds;
        plan.OfflineRecheckIntervalSeconds = ConvertOfflineRecheckToSeconds(PlanFormOfflineRecheckIntervalValue, PlanFormOfflineRecheckIntervalUnit);
        plan.MissedRunPolicy = PlanFormMissedRunPolicy;
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
        PlanFormScheduleMode = plan.ScheduleMode;
        ApplyFrequency(plan.IntervalMinutes);
        PlanFormFrequencyValue = plan.IntervalValue;
        PlanFormFrequencyUnit = plan.IntervalUnit.ToDisplayName();
        PlanFormTimesPerDay = plan.TimesPerDay <= 0 ? 4 : plan.TimesPerDay;
        PlanFormDailyTimes = plan.DailyTimes;
        PlanFormSelectedWeekDays = plan.SelectedWeekDays;
        PlanFormFailureRetryEnabled = plan.FailureRetryEnabled;
        PlanFormConfirmationRetryCount = plan.ConfirmationRetryCount;
        PlanFormConfirmationRetryIntervalSeconds = plan.ConfirmationRetryIntervalSeconds;
        ApplyOfflineRecheckInterval(plan.OfflineRecheckIntervalSeconds);
        PlanFormMissedRunPolicy = plan.MissedRunPolicy;
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
        PlanFormScheduleMode = ScheduleMode.FixedInterval;
        PlanFormFrequencyValue = AppSettings.DefaultSchedulePlanIntervalMinutes;
        PlanFormFrequencyUnit = "Dakika";
        PlanFormTimesPerDay = 4;
        PlanFormDailyTimes = "08:00;12:00;16:00;20:00";
        PlanFormSelectedWeekDays = "Monday,Tuesday,Wednesday,Thursday,Friday";
        PlanFormFailureRetryEnabled = true;
        PlanFormConfirmationRetryCount = AppSettings.DefaultFailureRetryLimitValue;
        PlanFormConfirmationRetryIntervalSeconds = AppSettings.DefaultFailureRetryIntervalSecondsValue;
        PlanFormOfflineRecheckIntervalValue = 20;
        PlanFormOfflineRecheckIntervalUnit = "Dakika";
        PlanFormMissedRunPolicy = MissedRunPolicy.SingleCatchUp;
        PlanFormTimeoutMs = PingTimeoutMs;
        PlanFormMaxParallelism = Math.Min(MaxParallelPings, AppSettings.DefaultSchedulePlanMaxParallelism);
        PlanFormFailureThreshold = DefaultFailureThreshold;
        PlanFormIsActive = true;
        PlanFormDescription = string.Empty;
        UpdatePlanTargetOptions();
        UpdatePlanPreview();
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
        var heartbeat = await _workerHeartbeatRepository.GetLatestAsync();
        var counts = await _notificationOutboxRepository.GetCountsAsync();
        PendingNotificationCount = counts.Pending;
        FailedNotificationCount = counts.Failed;

        WorkerVersionText = heartbeat?.Version ?? "-";
        WorkerStartedAtText = FormatLocal(heartbeat?.StartedAtUtc);
        WorkerLastSeenAtText = FormatLocal(heartbeat?.LastSeenAtUtc);
        WorkerLastSchedulerCycleText = FormatLocal(heartbeat?.LastSchedulerCycleAtUtc);
        WorkerLastScheduledPingText = FormatLocal(heartbeat?.LastSuccessfulPingAtUtc);
        WorkerLastNotificationText = FormatLocal(heartbeat?.LastNotificationDispatchAtUtc);

        var heartbeatAge = heartbeat is null ? TimeSpan.MaxValue : DateTime.UtcNow - heartbeat.LastSeenAtUtc;
        if (status.Code == "NotFound")
        {
            WorkerHealthText = "Kurulu değil";
        }
        else if (status.Code != "Running")
        {
            WorkerHealthText = status.DisplayText;
        }
        else if (heartbeat is null || heartbeatAge > TimeSpan.FromMinutes(2))
        {
            WorkerHealthText = "Gecikmiş";
        }
        else if (!string.IsNullOrWhiteSpace(heartbeat.LastError))
        {
            WorkerHealthText = "Hata";
        }
        else
        {
            WorkerHealthText = "Çalışıyor";
        }

        SchedulerStatusText = $"{status.DisplayText} / {WorkerHealthText}";
        IsSchedulerRunning = status.Code == "Running" && WorkerHealthText == "Çalışıyor";
    }

    public async Task<string> GetWorkerServiceStatusTextAsync()
    {
        await RefreshWorkerServiceStatusAsync();
        return SchedulerStatusText;
    }

    private static string FormatLocal(DateTime? value)
    {
        return value.HasValue ? value.Value.ToLocalTime().ToString("dd.MM.yyyy HH:mm:ss") : "-";
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
            "Gun" => normalized * 24 * 60,
            "Hafta" => normalized * 7 * 24 * 60,
            _ => normalized
        };
    }

    private static ScheduleIntervalUnit ConvertFrequencyUnit(string unit)
    {
        return unit switch
        {
            "Saat" => ScheduleIntervalUnit.Hours,
            "Gun" => ScheduleIntervalUnit.Days,
            "Hafta" => ScheduleIntervalUnit.Weeks,
            _ => ScheduleIntervalUnit.Minutes
        };
    }

    private static int ConvertOfflineRecheckToSeconds(int value, string unit)
    {
        var normalized = Math.Max(1, value);
        var seconds = unit switch
        {
            "Saat" => normalized * 60 * 60,
            "Gun" => normalized * 24 * 60 * 60,
            "Hafta" => normalized * 7 * 24 * 60 * 60,
            _ => normalized * 60
        };
        return Math.Clamp(seconds, AppSettings.MinOfflineRecheckIntervalSeconds, AppSettings.MaxOfflineRecheckIntervalSeconds);
    }

    private void ApplyFrequency(int intervalMinutes)
    {
        if (intervalMinutes % (24 * 60) == 0)
        {
            PlanFormFrequencyValue = Math.Max(1, intervalMinutes / (24 * 60));
            PlanFormFrequencyUnit = "Gun";
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

    private void ApplyOfflineRecheckInterval(int seconds)
    {
        if (seconds % (7 * 24 * 60 * 60) == 0)
        {
            PlanFormOfflineRecheckIntervalValue = Math.Max(1, seconds / (7 * 24 * 60 * 60));
            PlanFormOfflineRecheckIntervalUnit = "Hafta";
            return;
        }

        if (seconds % (24 * 60 * 60) == 0)
        {
            PlanFormOfflineRecheckIntervalValue = Math.Max(1, seconds / (24 * 60 * 60));
            PlanFormOfflineRecheckIntervalUnit = "Gun";
            return;
        }

        if (seconds % (60 * 60) == 0)
        {
            PlanFormOfflineRecheckIntervalValue = Math.Max(1, seconds / (60 * 60));
            PlanFormOfflineRecheckIntervalUnit = "Saat";
            return;
        }

        PlanFormOfflineRecheckIntervalValue = Math.Max(1, seconds / 60);
        PlanFormOfflineRecheckIntervalUnit = "Dakika";
    }

    private void UpdatePlanPreview()
    {
        var plan = new SchedulePlan
        {
            Name = PlanFormName,
            TargetType = PlanFormTargetType,
            TargetValue = PlanFormTargetValue,
            ScheduleMode = PlanFormScheduleMode,
            IntervalValue = PlanFormFrequencyValue,
            IntervalUnit = ConvertFrequencyUnit(PlanFormFrequencyUnit),
            IntervalMinutes = ConvertFrequencyToMinutes(PlanFormFrequencyValue, PlanFormFrequencyUnit),
            TimesPerDay = PlanFormTimesPerDay,
            DailyTimes = PlanFormDailyTimes,
            SelectedWeekDays = PlanFormSelectedWeekDays,
            TimeZoneId = TimeZoneInfo.Local.Id,
            FailureRetryEnabled = PlanFormFailureRetryEnabled,
            ConfirmationRetryCount = PlanFormConfirmationRetryCount,
            ConfirmationRetryIntervalSeconds = PlanFormConfirmationRetryIntervalSeconds,
            OfflineRecheckIntervalSeconds = ConvertOfflineRecheckToSeconds(PlanFormOfflineRecheckIntervalValue, PlanFormOfflineRecheckIntervalUnit),
            MissedRunPolicy = PlanFormMissedRunPolicy,
            TimeoutMs = PlanFormTimeoutMs,
            MaxParallelism = PlanFormMaxParallelism,
            FailureThreshold = PlanFormFailureThreshold,
            IsActive = PlanFormIsActive
        };

        var timing = new ScheduleTimingService();
        var validation = timing.Validate(plan);
        if (!validation.Success)
        {
            PlanFormPreviewText = validation.Message;
            return;
        }

        var nextRuns = timing.GetNextOccurrences(plan, DateTime.UtcNow, 5)
            .Select(value => value.ToLocalTime().ToString("dd.MM.yyyy HH:mm"))
            .ToList();
        PlanFormPreviewText =
            $"Plan kaynağı: {PlanFormTargetType.ToDisplayName()}\n" +
            $"Saat dilimi: {TimeZoneInfo.Local.DisplayName}\n" +
            $"Normal kontrol: {plan.ScheduleSummaryText}\n" +
            $"Erişilemeyen cihaz kontrolü: {plan.OfflineRecheckIntervalText}\n" +
            $"Hızlı retry: {plan.RetrySummaryText}\n" +
            $"Kaçırılmış kontroller: {plan.MissedRunPolicyText}\n" +
            $"Sonraki 5 kontrol:\n- {string.Join("\n- ", nextRuns)}";
    }
}


