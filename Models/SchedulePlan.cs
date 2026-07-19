using System.Globalization;
using NetworkHealthMonitor.Infrastructure;

namespace NetworkHealthMonitor.Models;

public sealed class SchedulePlan : ObservableObject
{
    private int _id;
    private string _name = string.Empty;
    private SchedulePlanTargetType _targetType = SchedulePlanTargetType.AllDevices;
    private string _targetValue = string.Empty;
    private string _targetDisplayName = string.Empty;
    private int _intervalMinutes = AppSettings.DefaultSchedulePlanIntervalMinutes;
    private ScheduleMode _scheduleMode = ScheduleMode.FixedInterval;
    private int _intervalValue = AppSettings.DefaultSchedulePlanIntervalMinutes;
    private ScheduleIntervalUnit _intervalUnit = ScheduleIntervalUnit.Minutes;
    private int _timesPerDay;
    private string _dailyTimes = string.Empty;
    private string _selectedWeekDays = string.Empty;
    private string _timeZoneId = TimeZoneInfo.Local.Id;
    private bool _failureRetryEnabled = true;
    private int _confirmationRetryCount = AppSettings.DefaultFailureRetryLimitValue;
    private int _confirmationRetryIntervalSeconds = AppSettings.DefaultFailureRetryIntervalSecondsValue;
    private int _offlineRecheckIntervalSeconds = AppSettings.DefaultOfflineRecheckIntervalSeconds;
    private MissedRunPolicy _missedRunPolicy = MissedRunPolicy.SingleCatchUp;
    private int _timeoutMs = AppSettings.DefaultPingTimeoutMs;
    private int _maxParallelism = AppSettings.DefaultSchedulePlanMaxParallelism;
    private int _failureThreshold = AppSettings.DefaultFailureThresholdValue;
    private bool _isActive = true;
    private string _description = string.Empty;
    private DateTime? _lastRunAt;
    private DateTime? _nextRunAt;
    private string _lastStatus = string.Empty;
    private DateTime _createdAt = DateTime.UtcNow;
    private DateTime _updatedAt = DateTime.UtcNow;

    public int Id
    {
        get => _id;
        set => SetProperty(ref _id, value);
    }

    public string Name
    {
        get => _name;
        set => SetProperty(ref _name, value ?? string.Empty);
    }

    public SchedulePlanTargetType TargetType
    {
        get => _targetType;
        set
        {
            if (SetProperty(ref _targetType, value))
            {
                OnPropertyChanged(nameof(TargetTypeText));
            }
        }
    }

    public string TargetValue
    {
        get => _targetValue;
        set => SetProperty(ref _targetValue, value ?? string.Empty);
    }

    public string TargetDisplayName
    {
        get => string.IsNullOrWhiteSpace(_targetDisplayName) ? TargetValue : _targetDisplayName;
        set => SetProperty(ref _targetDisplayName, value ?? string.Empty);
    }

    public int IntervalMinutes
    {
        get => _intervalMinutes;
        set
        {
            var normalized = Math.Clamp(value, AppSettings.MinSchedulePlanIntervalMinutes, AppSettings.MaxSchedulePlanIntervalMinutes);
            if (SetProperty(ref _intervalMinutes, normalized))
            {
                if (ScheduleMode == ScheduleMode.FixedInterval)
                {
                    SetIntervalFieldsFromMinutes(normalized);
                }

                NotifyScheduleTextChanged();
            }
        }
    }

    public ScheduleMode ScheduleMode
    {
        get => _scheduleMode;
        set
        {
            if (SetProperty(ref _scheduleMode, value))
            {
                NotifyScheduleTextChanged();
            }
        }
    }

    public int IntervalValue
    {
        get => _intervalValue;
        set
        {
            if (SetProperty(ref _intervalValue, value))
            {
                RecalculateIntervalMinutes();
                NotifyScheduleTextChanged();
            }
        }
    }

    public ScheduleIntervalUnit IntervalUnit
    {
        get => _intervalUnit;
        set
        {
            if (SetProperty(ref _intervalUnit, value))
            {
                RecalculateIntervalMinutes();
                NotifyScheduleTextChanged();
            }
        }
    }

    public int TimesPerDay
    {
        get => _timesPerDay;
        set
        {
            if (SetProperty(ref _timesPerDay, value))
            {
                NotifyScheduleTextChanged();
            }
        }
    }

    public string DailyTimes
    {
        get => _dailyTimes;
        set
        {
            if (SetProperty(ref _dailyTimes, value ?? string.Empty))
            {
                NotifyScheduleTextChanged();
            }
        }
    }

    public string SelectedWeekDays
    {
        get => _selectedWeekDays;
        set
        {
            if (SetProperty(ref _selectedWeekDays, value ?? string.Empty))
            {
                NotifyScheduleTextChanged();
            }
        }
    }

    public string TimeZoneId
    {
        get => string.IsNullOrWhiteSpace(_timeZoneId) ? TimeZoneInfo.Local.Id : _timeZoneId;
        set => SetProperty(ref _timeZoneId, string.IsNullOrWhiteSpace(value) ? TimeZoneInfo.Local.Id : value);
    }

    public bool FailureRetryEnabled
    {
        get => _failureRetryEnabled;
        set
        {
            if (SetProperty(ref _failureRetryEnabled, value))
            {
                NotifyScheduleTextChanged();
            }
        }
    }

    public int ConfirmationRetryCount
    {
        get => _confirmationRetryCount;
        set => SetProperty(ref _confirmationRetryCount, value);
    }

    public int ConfirmationRetryIntervalSeconds
    {
        get => _confirmationRetryIntervalSeconds;
        set => SetProperty(ref _confirmationRetryIntervalSeconds, value);
    }

    public int OfflineRecheckIntervalSeconds
    {
        get => _offlineRecheckIntervalSeconds;
        set
        {
            if (SetProperty(ref _offlineRecheckIntervalSeconds, value))
            {
                OnPropertyChanged(nameof(OfflineRecheckIntervalText));
            }
        }
    }

    public MissedRunPolicy MissedRunPolicy
    {
        get => _missedRunPolicy;
        set
        {
            if (SetProperty(ref _missedRunPolicy, value))
            {
                NotifyScheduleTextChanged();
            }
        }
    }

    public int TimeoutMs
    {
        get => _timeoutMs;
        set => SetProperty(ref _timeoutMs, Math.Clamp(value, AppSettings.MinPingTimeoutMs, AppSettings.MaxPingTimeoutMs));
    }

    public int MaxParallelism
    {
        get => _maxParallelism;
        set => SetProperty(ref _maxParallelism, Math.Clamp(value, AppSettings.MinParallelPings, AppSettings.MaxParallelPingsLimit));
    }

    public int FailureThreshold
    {
        get => _failureThreshold;
        set => SetProperty(ref _failureThreshold, Math.Clamp(value, AppSettings.MinFailureThreshold, AppSettings.MaxFailureThreshold));
    }

    public bool IsActive
    {
        get => _isActive;
        set
        {
            if (SetProperty(ref _isActive, value))
            {
                OnPropertyChanged(nameof(IsActiveText));
            }
        }
    }

    public string Description
    {
        get => _description;
        set => SetProperty(ref _description, value ?? string.Empty);
    }

    public DateTime? LastRunAt
    {
        get => _lastRunAt;
        set
        {
            if (SetProperty(ref _lastRunAt, value))
            {
                OnPropertyChanged(nameof(LastRunAtText));
            }
        }
    }

    public DateTime? NextRunAt
    {
        get => _nextRunAt;
        set
        {
            if (SetProperty(ref _nextRunAt, value))
            {
                OnPropertyChanged(nameof(NextRunAtText));
            }
        }
    }

    public string LastStatus
    {
        get => _lastStatus;
        set => SetProperty(ref _lastStatus, value ?? string.Empty);
    }

    public DateTime CreatedAt
    {
        get => _createdAt;
        set => SetProperty(ref _createdAt, value.ToUniversalTime());
    }

    public DateTime UpdatedAt
    {
        get => _updatedAt;
        set => SetProperty(ref _updatedAt, value.ToUniversalTime());
    }

    public string TargetTypeText => TargetType.ToDisplayName();

    public string ScheduleModeText => ScheduleMode.ToDisplayName();

    public string IntervalText => ScheduleMode == ScheduleMode.FixedInterval
        ? FormatDuration(TimeSpan.FromMinutes(Math.Max(1, IntervalMinutes)))
        : ScheduleSummaryText;

    public string ScheduleSummaryText
    {
        get
        {
            return ScheduleMode switch
            {
                ScheduleMode.FixedInterval => $"Her {FormatDuration(TimeSpan.FromMinutes(Math.Max(1, IntervalMinutes)))}",
                ScheduleMode.TimesPerDay => string.IsNullOrWhiteSpace(DailyTimes)
                    ? $"Günde {Math.Max(1, TimesPerDay)} kez eşit dağıtılmış"
                    : $"Günde {Math.Max(1, TimesPerDay)} kez: {DailyTimes}",
                ScheduleMode.DailyTimes => $"Her gün: {DailyTimes}",
                ScheduleMode.Weekly => $"Haftalık: {SelectedWeekDays} / {DailyTimes}",
                _ => $"Her {FormatDuration(TimeSpan.FromMinutes(Math.Max(1, IntervalMinutes)))}"
            };
        }
    }

    public string OfflineRecheckIntervalText => FormatDuration(TimeSpan.FromSeconds(OfflineRecheckIntervalSeconds));

    public string RetrySummaryText => FailureRetryEnabled
        ? $"{ConfirmationRetryCount} hızlı retry, {FormatDuration(TimeSpan.FromSeconds(ConfirmationRetryIntervalSeconds))} aralık"
        : "Hızlı retry kapalı";

    public string MissedRunPolicyText => MissedRunPolicy.ToDisplayName();

    public string IsActiveText => UiDisplayTexts.ActiveState(IsActive);

    public string LastRunAtText => LastRunAt.HasValue ? LastRunAt.Value.ToLocalTime().ToString("dd.MM.yyyy HH:mm:ss", CultureInfo.CurrentCulture) : "-";

    public string NextRunAtText => NextRunAt.HasValue ? NextRunAt.Value.ToLocalTime().ToString("dd.MM.yyyy HH:mm:ss", CultureInfo.CurrentCulture) : "-";

    public PingOptions ToPingOptions()
    {
        return new PingOptions(TimeoutMs, MaxParallelism, FailureThreshold);
    }

    private void RecalculateIntervalMinutes()
    {
        if (ScheduleMode != ScheduleMode.FixedInterval)
        {
            return;
        }

        var minutes = IntervalUnit switch
        {
            ScheduleIntervalUnit.Hours => IntervalValue * 60,
            ScheduleIntervalUnit.Days => IntervalValue * 24 * 60,
            ScheduleIntervalUnit.Weeks => IntervalValue * 7 * 24 * 60,
            _ => IntervalValue
        };

        _intervalMinutes = Math.Clamp(minutes, AppSettings.MinSchedulePlanIntervalMinutes, AppSettings.MaxSchedulePlanIntervalMinutes);
        OnPropertyChanged(nameof(IntervalMinutes));
    }

    private void SetIntervalFieldsFromMinutes(int intervalMinutes)
    {
        if (intervalMinutes % (7 * 24 * 60) == 0)
        {
            _intervalValue = Math.Max(1, intervalMinutes / (7 * 24 * 60));
            _intervalUnit = ScheduleIntervalUnit.Weeks;
        }
        else if (intervalMinutes % (24 * 60) == 0)
        {
            _intervalValue = Math.Max(1, intervalMinutes / (24 * 60));
            _intervalUnit = ScheduleIntervalUnit.Days;
        }
        else if (intervalMinutes % 60 == 0)
        {
            _intervalValue = Math.Max(1, intervalMinutes / 60);
            _intervalUnit = ScheduleIntervalUnit.Hours;
        }
        else
        {
            _intervalValue = Math.Max(1, intervalMinutes);
            _intervalUnit = ScheduleIntervalUnit.Minutes;
        }

        OnPropertyChanged(nameof(IntervalValue));
        OnPropertyChanged(nameof(IntervalUnit));
    }

    private void NotifyScheduleTextChanged()
    {
        OnPropertyChanged(nameof(ScheduleModeText));
        OnPropertyChanged(nameof(IntervalText));
        OnPropertyChanged(nameof(ScheduleSummaryText));
        OnPropertyChanged(nameof(RetrySummaryText));
        OnPropertyChanged(nameof(MissedRunPolicyText));
    }

    private static string FormatDuration(TimeSpan duration)
    {
        if (duration.TotalDays >= 1 && Math.Abs(duration.TotalDays - Math.Round(duration.TotalDays)) < 0.001)
        {
            return $"{(int)Math.Round(duration.TotalDays)} gün";
        }

        if (duration.TotalHours >= 1 && Math.Abs(duration.TotalHours - Math.Round(duration.TotalHours)) < 0.001)
        {
            return $"{(int)Math.Round(duration.TotalHours)} saat";
        }

        return $"{Math.Max(1, (int)Math.Round(duration.TotalMinutes))} dakika";
    }
}
