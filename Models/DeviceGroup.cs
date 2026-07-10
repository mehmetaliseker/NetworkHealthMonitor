using NetworkHealthMonitor.Infrastructure;

namespace NetworkHealthMonitor.Models;

public sealed class DeviceGroup : ObservableObject
{
    private int _id;
    private string _name = string.Empty;
    private string _description = string.Empty;
    private int? _defaultSchedulePlanId;
    private int? _defaultCheckIntervalSeconds;
    private DateTime _createdAt = DateTime.Now;
    private DateTime _updatedAt = DateTime.Now;
    private int _deviceCount;
    private double? _availability30DaysPercent;

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

    public string Description
    {
        get => _description;
        set => SetProperty(ref _description, value ?? string.Empty);
    }

    public int? DefaultSchedulePlanId
    {
        get => _defaultSchedulePlanId;
        set => SetProperty(ref _defaultSchedulePlanId, value);
    }

    public int? DefaultCheckIntervalSeconds
    {
        get => _defaultCheckIntervalSeconds;
        set
        {
            var normalized = value.HasValue
                ? Math.Clamp(value.Value, AppSettings.MinDeviceCheckIntervalSeconds, AppSettings.MaxDeviceCheckIntervalSeconds)
                : null;
            if (SetProperty(ref _defaultCheckIntervalSeconds, normalized))
            {
                OnPropertyChanged(nameof(DefaultCheckIntervalText));
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

    public int DeviceCount
    {
        get => _deviceCount;
        set
        {
            if (SetProperty(ref _deviceCount, value))
            {
                OnPropertyChanged(nameof(DeviceCountText));
            }
        }
    }

    public double? Availability30DaysPercent
    {
        get => _availability30DaysPercent;
        set
        {
            if (SetProperty(ref _availability30DaysPercent, value))
            {
                OnPropertyChanged(nameof(Availability30DaysText));
            }
        }
    }

    public string DeviceCountText => DeviceCount.ToString();

    public string Availability30DaysText => Availability30DaysPercent.HasValue ? $"{Availability30DaysPercent.Value:0.0}%" : "-";

    public string DefaultCheckIntervalText => DefaultCheckIntervalSeconds.HasValue ? FormatDuration(DefaultCheckIntervalSeconds.Value) : "Plan/global";

    private static string FormatDuration(int seconds)
    {
        if (seconds >= 3600 && seconds % 3600 == 0)
        {
            return $"{seconds / 3600} sa";
        }

        if (seconds >= 60 && seconds % 60 == 0)
        {
            return $"{seconds / 60} dk";
        }

        return $"{seconds} sn";
    }
}
