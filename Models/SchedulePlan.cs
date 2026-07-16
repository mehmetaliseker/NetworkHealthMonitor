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
    private int _timeoutMs = AppSettings.DefaultPingTimeoutMs;
    private int _maxParallelism = AppSettings.DefaultSchedulePlanMaxParallelism;
    private int _failureThreshold = AppSettings.DefaultFailureThresholdValue;
    private bool _isActive = true;
    private string _description = string.Empty;
    private DateTime? _lastRunAt;
    private DateTime? _nextRunAt;
    private string _lastStatus = string.Empty;
    private DateTime _createdAt = DateTime.Now;
    private DateTime _updatedAt = DateTime.Now;

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
            if (SetProperty(ref _intervalMinutes, Math.Max(1, value)))
            {
                OnPropertyChanged(nameof(IntervalText));
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
        set => SetProperty(ref _createdAt, value);
    }

    public DateTime UpdatedAt
    {
        get => _updatedAt;
        set => SetProperty(ref _updatedAt, value);
    }

    public string TargetTypeText => TargetType.ToDisplayName();

    public string IntervalText => $"{IntervalMinutes} dk";

    public string IsActiveText => UiDisplayTexts.ActiveState(IsActive);

    public string LastRunAtText => LastRunAt.HasValue ? LastRunAt.Value.ToString("dd.MM.yyyy HH:mm:ss") : "-";

    public string NextRunAtText => NextRunAt.HasValue ? NextRunAt.Value.ToString("dd.MM.yyyy HH:mm:ss") : "-";

    public PingOptions ToPingOptions()
    {
        return new PingOptions(TimeoutMs, MaxParallelism, FailureThreshold);
    }
}
