using NetworkHealthMonitor.Infrastructure;
using System.Text.Json.Serialization;

namespace NetworkHealthMonitor.Models;

public sealed class DeviceTypePolicy : ObservableObject
{
    private DeviceType _deviceType;
    private bool? _autoCheckEnabled;
    private int? _defaultCheckIntervalSeconds;
    private int? _defaultPingTimeoutMs;
    private int? _defaultFailureRetryIntervalSeconds;
    private int? _defaultFailureRetryLimit;
    private int? _defaultFailureThreshold;

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

    [JsonIgnore]
    public string DeviceTypeText => DeviceType.ToDisplayName();

    public bool? AutoCheckEnabled
    {
        get => _autoCheckEnabled;
        set => SetProperty(ref _autoCheckEnabled, value);
    }

    public int? DefaultCheckIntervalSeconds
    {
        get => _defaultCheckIntervalSeconds;
        set => SetProperty(ref _defaultCheckIntervalSeconds, NormalizeNullable(value, AppSettings.MinDeviceCheckIntervalSeconds, AppSettings.MaxDeviceCheckIntervalSeconds));
    }

    public int? DefaultPingTimeoutMs
    {
        get => _defaultPingTimeoutMs;
        set => SetProperty(ref _defaultPingTimeoutMs, NormalizeNullable(value, AppSettings.MinPingTimeoutMs, AppSettings.MaxPingTimeoutMs));
    }

    public int? DefaultFailureRetryIntervalSeconds
    {
        get => _defaultFailureRetryIntervalSeconds;
        set => SetProperty(ref _defaultFailureRetryIntervalSeconds, NormalizeNullable(value, AppSettings.MinFailureRetryIntervalSeconds, AppSettings.MaxFailureRetryIntervalSeconds));
    }

    public int? DefaultFailureRetryLimit
    {
        get => _defaultFailureRetryLimit;
        set => SetProperty(ref _defaultFailureRetryLimit, NormalizeNullable(value, AppSettings.MinFailureRetryLimit, AppSettings.MaxFailureRetryLimit));
    }

    public int? DefaultFailureThreshold
    {
        get => _defaultFailureThreshold;
        set => SetProperty(ref _defaultFailureThreshold, NormalizeNullable(value, AppSettings.MinFailureThreshold, AppSettings.MaxFailureThreshold));
    }

    public static IReadOnlyList<DeviceTypePolicy> CreateDefaults()
    {
        return Enum.GetValues<DeviceType>()
            .Select(type => new DeviceTypePolicy { DeviceType = type })
            .ToList();
    }

    public static List<DeviceTypePolicy> NormalizeCollection(IEnumerable<DeviceTypePolicy>? policies)
    {
        var byType = (policies ?? Array.Empty<DeviceTypePolicy>())
            .GroupBy(policy => policy.DeviceType)
            .ToDictionary(group => group.Key, group => group.First());

        return Enum.GetValues<DeviceType>()
            .Select(type =>
            {
                byType.TryGetValue(type, out var policy);
                return new DeviceTypePolicy
                {
                    DeviceType = type,
                    AutoCheckEnabled = policy?.AutoCheckEnabled,
                    DefaultCheckIntervalSeconds = policy?.DefaultCheckIntervalSeconds,
                    DefaultPingTimeoutMs = policy?.DefaultPingTimeoutMs,
                    DefaultFailureRetryIntervalSeconds = policy?.DefaultFailureRetryIntervalSeconds,
                    DefaultFailureRetryLimit = policy?.DefaultFailureRetryLimit,
                    DefaultFailureThreshold = policy?.DefaultFailureThreshold
                };
            })
            .ToList();
    }

    private static int? NormalizeNullable(int? value, int minimum, int maximum)
    {
        return value.HasValue && value.Value > 0
            ? Math.Clamp(value.Value, minimum, maximum)
            : null;
    }
}
