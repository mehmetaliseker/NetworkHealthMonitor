using System.Globalization;
using NetworkHealthMonitor.Infrastructure;

namespace NetworkHealthMonitor.Models;

public sealed class Device : ObservableObject
{
    private int _id;
    private string _name = string.Empty;
    private string _ipAddress = string.Empty;
    private DeviceType _deviceType = DeviceType.Camera;
    private string _location = string.Empty;
    private int? _groupId;
    private string _groupName = string.Empty;
    private bool _isCritical;
    private bool _isActive = true;
    private bool _autoCheckEnabled = true;
    private int? _defaultSchedulePlanId;
    private string _description = string.Empty;
    private DeviceStatus _lastStatus = DeviceStatus.NotChecked;
    private long? _lastLatencyMs;
    private DateTime? _lastCheckedAt;
    private int _consecutiveFailures;
    private int _consecutiveSuccesses;
    private DeviceStatus _lastStableStatus = DeviceStatus.NotChecked;
    private DateTime _createdAt = DateTime.Now;
    private DateTime _updatedAt = DateTime.Now;
    private double? _uptime24HoursPercent;
    private double? _uptime7DaysPercent;
    private double? _uptime30DaysPercent;
    private long? _averageLatencyMs;
    private DateTime? _lastFailureAt;

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

    public string IpAddress
    {
        get => _ipAddress;
        set
        {
            if (SetProperty(ref _ipAddress, value ?? string.Empty))
            {
                OnPropertyChanged(nameof(IpSortKey));
            }
        }
    }

    public long IpSortKey => CreateIpSortKey(IpAddress);

    public DeviceType DeviceType
    {
        get => _deviceType;
        set
        {
            if (SetProperty(ref _deviceType, value))
            {
                OnPropertyChanged(nameof(DeviceTypeText));
            }
        }
    }

    public string Location
    {
        get => _location;
        set => SetProperty(ref _location, value ?? string.Empty);
    }

    public int? GroupId
    {
        get => _groupId;
        set => SetProperty(ref _groupId, value);
    }

    public string GroupName
    {
        get => _groupName;
        set => SetProperty(ref _groupName, value ?? string.Empty);
    }

    public bool IsCritical
    {
        get => _isCritical;
        set
        {
            if (SetProperty(ref _isCritical, value))
            {
                OnPropertyChanged(nameof(IsCriticalText));
            }
        }
    }

    public string IsCriticalText => IsCritical ? "Kritik" : "-";

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

    public string IsActiveText => IsActive ? "Aktif" : "Pasif";

    public bool AutoCheckEnabled
    {
        get => _autoCheckEnabled;
        set
        {
            if (SetProperty(ref _autoCheckEnabled, value))
            {
                OnPropertyChanged(nameof(AutoCheckText));
            }
        }
    }

    public string AutoCheckText => AutoCheckEnabled ? "Açık" : "Kapalı";

    public int? DefaultSchedulePlanId
    {
        get => _defaultSchedulePlanId;
        set => SetProperty(ref _defaultSchedulePlanId, value);
    }

    public string Description
    {
        get => _description;
        set
        {
            if (SetProperty(ref _description, value ?? string.Empty))
            {
                OnPropertyChanged(nameof(Note));
            }
        }
    }

    public string Note
    {
        get => Description;
        set => Description = value;
    }

    public DeviceStatus LastStatus
    {
        get => _lastStatus;
        set
        {
            if (SetProperty(ref _lastStatus, value))
            {
                OnPropertyChanged(nameof(LastStatusText));
                OnPropertyChanged(nameof(IsProblematic));
            }
        }
    }

    public long? LastLatencyMs
    {
        get => _lastLatencyMs;
        set
        {
            if (SetProperty(ref _lastLatencyMs, value))
            {
                OnPropertyChanged(nameof(LastLatencyText));
            }
        }
    }

    public DateTime? LastCheckedAt
    {
        get => _lastCheckedAt;
        set
        {
            if (SetProperty(ref _lastCheckedAt, value))
            {
                OnPropertyChanged(nameof(LastCheckedAtText));
            }
        }
    }

    public int ConsecutiveFailures
    {
        get => _consecutiveFailures;
        set
        {
            if (SetProperty(ref _consecutiveFailures, value))
            {
                OnPropertyChanged(nameof(ConsecutiveFailuresText));
                OnPropertyChanged(nameof(IsProblematic));
            }
        }
    }

    public int ConsecutiveSuccesses
    {
        get => _consecutiveSuccesses;
        set => SetProperty(ref _consecutiveSuccesses, value);
    }

    public DeviceStatus LastStableStatus
    {
        get => _lastStableStatus;
        set
        {
            if (SetProperty(ref _lastStableStatus, value))
            {
                OnPropertyChanged(nameof(LastStableStatusText));
            }
        }
    }

    public bool IsProblematic => ConsecutiveFailures >= 3;

    public string ConsecutiveFailuresText => ConsecutiveFailures.ToString(CultureInfo.CurrentCulture);

    public string LastStableStatusText => LastStableStatus.ToDisplayName();

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

    public double? Uptime24HoursPercent
    {
        get => _uptime24HoursPercent;
        set
        {
            if (SetProperty(ref _uptime24HoursPercent, value))
            {
                OnPropertyChanged(nameof(Uptime24HoursText));
            }
        }
    }

    public double? Uptime7DaysPercent
    {
        get => _uptime7DaysPercent;
        set
        {
            if (SetProperty(ref _uptime7DaysPercent, value))
            {
                OnPropertyChanged(nameof(Uptime7DaysText));
            }
        }
    }

    public double? Uptime30DaysPercent
    {
        get => _uptime30DaysPercent;
        set
        {
            if (SetProperty(ref _uptime30DaysPercent, value))
            {
                OnPropertyChanged(nameof(Uptime30DaysText));
            }
        }
    }

    public long? AverageLatencyMs
    {
        get => _averageLatencyMs;
        set
        {
            if (SetProperty(ref _averageLatencyMs, value))
            {
                OnPropertyChanged(nameof(AverageLatencyText));
            }
        }
    }

    public DateTime? LastFailureAt
    {
        get => _lastFailureAt;
        set
        {
            if (SetProperty(ref _lastFailureAt, value))
            {
                OnPropertyChanged(nameof(LastFailureAtText));
            }
        }
    }

    public string DeviceTypeText => DeviceType.ToDisplayName();

    public string LastStatusText => LastStatus.ToDisplayName();

    public string LastLatencyText => LastLatencyMs.HasValue ? $"{LastLatencyMs.Value} ms" : "-";

    public string LastCheckedAtText => LastCheckedAt.HasValue ? LastCheckedAt.Value.ToString("dd.MM.yyyy HH:mm:ss", CultureInfo.CurrentCulture) : "-";

    public string Uptime24HoursText => FormatPercent(Uptime24HoursPercent);

    public string Uptime7DaysText => FormatPercent(Uptime7DaysPercent);

    public string Uptime30DaysText => FormatPercent(Uptime30DaysPercent);

    public string AverageLatencyText => AverageLatencyMs.HasValue ? $"{AverageLatencyMs.Value} ms" : "-";

    public string LastFailureAtText => LastFailureAt.HasValue ? LastFailureAt.Value.ToString("dd.MM.yyyy HH:mm:ss", CultureInfo.CurrentCulture) : "-";

    private static string FormatPercent(double? value)
    {
        return value.HasValue ? $"{value.Value:0.0}%" : "-";
    }

    private static long CreateIpSortKey(string ipAddress)
    {
        var parts = ipAddress.Split('.');
        if (parts.Length != 4)
        {
            return long.MaxValue;
        }

        var result = 0L;
        foreach (var part in parts)
        {
            if (!byte.TryParse(part, out var value))
            {
                return long.MaxValue;
            }

            result = (result << 8) + value;
        }

        return result;
    }
}
