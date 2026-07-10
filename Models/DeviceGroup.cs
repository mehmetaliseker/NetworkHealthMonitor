using NetworkHealthMonitor.Infrastructure;

namespace NetworkHealthMonitor.Models;

public sealed class DeviceGroup : ObservableObject
{
    private int _id;
    private string _name = string.Empty;
    private string _description = string.Empty;
    private int? _defaultSchedulePlanId;
    private bool? _defaultAutoCheckEnabled;
    private int? _defaultCheckIntervalSeconds;
    private int? _defaultPingTimeoutMs;
    private int? _defaultFailureRetryIntervalSeconds;
    private int? _defaultFailureRetryLimit;
    private int? _defaultFailureThreshold;
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

    public bool? DefaultAutoCheckEnabled
    {
        get => _defaultAutoCheckEnabled;
        set
        {
            if (SetProperty(ref _defaultAutoCheckEnabled, value))
            {
                OnPropertyChanged(nameof(DefaultAutoCheckText));
                OnPropertyChanged(nameof(DefaultPolicyText));
            }
        }
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
                OnPropertyChanged(nameof(DefaultPolicyText));
            }
        }
    }

    public int? DefaultPingTimeoutMs
    {
        get => _defaultPingTimeoutMs;
        set
        {
            if (SetProperty(ref _defaultPingTimeoutMs, NormalizeNullable(value, AppSettings.MinPingTimeoutMs, AppSettings.MaxPingTimeoutMs)))
            {
                OnPropertyChanged(nameof(DefaultPolicyText));
            }
        }
    }

    public int? DefaultFailureRetryIntervalSeconds
    {
        get => _defaultFailureRetryIntervalSeconds;
        set
        {
            if (SetProperty(ref _defaultFailureRetryIntervalSeconds, NormalizeNullable(value, AppSettings.MinFailureRetryIntervalSeconds, AppSettings.MaxFailureRetryIntervalSeconds)))
            {
                OnPropertyChanged(nameof(DefaultPolicyText));
            }
        }
    }

    public int? DefaultFailureRetryLimit
    {
        get => _defaultFailureRetryLimit;
        set
        {
            if (SetProperty(ref _defaultFailureRetryLimit, NormalizeNullable(value, AppSettings.MinFailureRetryLimit, AppSettings.MaxFailureRetryLimit)))
            {
                OnPropertyChanged(nameof(DefaultPolicyText));
            }
        }
    }

    public int? DefaultFailureThreshold
    {
        get => _defaultFailureThreshold;
        set
        {
            if (SetProperty(ref _defaultFailureThreshold, NormalizeNullable(value, AppSettings.MinFailureThreshold, AppSettings.MaxFailureThreshold)))
            {
                OnPropertyChanged(nameof(DefaultPolicyText));
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

    public string DefaultAutoCheckText => DefaultAutoCheckEnabled.HasValue
        ? (DefaultAutoCheckEnabled.Value ? "Açık" : "Kapalı")
        : "Tip/global";

    public string DefaultCheckIntervalText => DefaultCheckIntervalSeconds.HasValue ? FormatDuration(DefaultCheckIntervalSeconds.Value) : "Tip/plan/global";

    public string DefaultPolicyText
    {
        get
        {
            var parts = new List<string>();
            if (DefaultAutoCheckEnabled.HasValue)
            {
                parts.Add($"oto {DefaultAutoCheckText}");
            }

            if (DefaultCheckIntervalSeconds.HasValue)
            {
                parts.Add($"aralık {FormatDuration(DefaultCheckIntervalSeconds.Value)}");
            }

            if (DefaultPingTimeoutMs.HasValue)
            {
                parts.Add($"timeout {DefaultPingTimeoutMs.Value} ms");
            }

            if (DefaultFailureRetryIntervalSeconds.HasValue)
            {
                parts.Add($"retry {FormatDuration(DefaultFailureRetryIntervalSeconds.Value)}");
            }

            if (DefaultFailureRetryLimit.HasValue)
            {
                parts.Add($"limit {DefaultFailureRetryLimit.Value}");
            }

            if (DefaultFailureThreshold.HasValue)
            {
                parts.Add($"eşik {DefaultFailureThreshold.Value}");
            }

            return parts.Count == 0 ? "Tip/global" : string.Join(", ", parts);
        }
    }

    private static int? NormalizeNullable(int? value, int minimum, int maximum)
    {
        return value.HasValue && value.Value > 0
            ? Math.Clamp(value.Value, minimum, maximum)
            : null;
    }

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
