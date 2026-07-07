using NetworkHealthMonitor.Infrastructure;

namespace NetworkHealthMonitor.Models;

public sealed class SchedulePlan : ObservableObject
{
    private int _id;
    private string _name = string.Empty;
    private SchedulePlanTargetType _targetType = SchedulePlanTargetType.AllDevices;
    private string _targetValue = string.Empty;
    private string _targetDisplayName = string.Empty;
    private int _intervalMinutes = 10;
    private int _timeoutMs = 1000;
    private int _maxParallelism = 16;
    private int _failureThreshold = 3;
    private bool _isActive = true;
    private string _description = string.Empty;
    private DateTime? _lastRunAt;
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
        set => SetProperty(ref _timeoutMs, Math.Clamp(value, 250, 10000));
    }

    public int MaxParallelism
    {
        get => _maxParallelism;
        set => SetProperty(ref _maxParallelism, Math.Clamp(value, 1, 128));
    }

    public int FailureThreshold
    {
        get => _failureThreshold;
        set => SetProperty(ref _failureThreshold, Math.Clamp(value, 1, 20));
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

    public string IsActiveText => IsActive ? "Aktif" : "Pasif";

    public string LastRunAtText => LastRunAt.HasValue ? LastRunAt.Value.ToString("dd.MM.yyyy HH:mm:ss") : "-";

    public PingOptions ToPingOptions()
    {
        return new PingOptions(TimeoutMs, MaxParallelism, FailureThreshold);
    }
}
