using NetworkHealthMonitor.Models;

namespace NetworkHealthMonitor.Services;

public sealed class DeviceCheckPolicyService : IDeviceCheckPolicyService
{
    public DeviceCheckPolicy ResolvePolicy(
        Device device,
        DeviceGroup? group,
        SchedulePlan? schedulePlan,
        AppSettings settings,
        PingOptions? options = null)
    {
        var typePolicy = settings.DeviceTypePolicies.FirstOrDefault(policy => policy.DeviceType == device.DeviceType);
        var autoCheckEnabled = ResolveAutoCheckEnabled(device, group, typePolicy, settings, out var autoSource);
        var normalIntervalSeconds = ResolveNormalIntervalSeconds(device, group, typePolicy, schedulePlan, settings, out var intervalSource);
        var pingTimeoutMs = ResolvePingTimeoutMs(device, group, typePolicy, schedulePlan, settings, options, out var timeoutSource);
        var retryIntervalSeconds = ResolveRetryIntervalSeconds(device, group, typePolicy, settings, out var retryIntervalSource);
        var retryLimit = ResolveRetryLimit(device, group, typePolicy, settings, out var retryLimitSource);
        var failureThreshold = ResolveFailureThreshold(device, group, typePolicy, schedulePlan, settings, options, out var thresholdSource);

        return new DeviceCheckPolicy(
            autoCheckEnabled,
            normalIntervalSeconds,
            pingTimeoutMs,
            retryIntervalSeconds,
            retryLimit,
            failureThreshold,
            $"Auto: {autoSource}, Aralık: {intervalSource}, Timeout: {timeoutSource}, Retry: {retryIntervalSource}/{retryLimitSource}, Eşik: {thresholdSource}");
    }

    public bool IsDue(Device device, DeviceCheckPolicy policy, DateTime now)
    {
        if (!device.IsActive || !policy.AutoCheckEnabled)
        {
            return false;
        }

        var nextCheckAt = GetNextCheckAt(device, policy);
        return !nextCheckAt.HasValue || nextCheckAt.Value <= now;
    }

    public DateTime? GetNextCheckAt(Device device, DeviceCheckPolicy policy)
    {
        var lastCheckAt = device.LastCheckedAt;
        if (!lastCheckAt.HasValue)
        {
            return null;
        }

        var intervalSeconds = ShouldUseFailureRetry(device, policy)
            ? policy.FailureRetryIntervalSeconds
            : policy.NormalIntervalSeconds;

        return lastCheckAt.Value.AddSeconds(intervalSeconds);
    }

    private static bool ShouldUseFailureRetry(Device device, DeviceCheckPolicy policy)
    {
        return policy.HasFailureRetryRemaining(device.ConsecutiveFailures)
            && device.LastStatus.IsFailureObservation();
    }

    private static bool ResolveAutoCheckEnabled(
        Device device,
        DeviceGroup? group,
        DeviceTypePolicy? typePolicy,
        AppSettings settings,
        out string source)
    {
        if (!device.AutoCheckEnabled)
        {
            source = "Cihaz özel";
            return false;
        }

        if (group?.DefaultAutoCheckEnabled.HasValue == true)
        {
            source = "Grup";
            return group.DefaultAutoCheckEnabled.Value;
        }

        if (typePolicy?.AutoCheckEnabled.HasValue == true)
        {
            source = "Tip";
            return typePolicy.AutoCheckEnabled.Value;
        }

        source = "Global";
        return settings.AutoCheckEnabled;
    }

    private static int ResolveNormalIntervalSeconds(
        Device device,
        DeviceGroup? group,
        DeviceTypePolicy? typePolicy,
        SchedulePlan? schedulePlan,
        AppSettings settings,
        out string source)
    {
        if (device.CheckIntervalSeconds > 0)
        {
            source = "Cihaz özel";
            return device.CheckIntervalSeconds;
        }

        if (group?.DefaultCheckIntervalSeconds is > 0)
        {
            source = "Grup";
            return group.DefaultCheckIntervalSeconds.Value;
        }

        if (typePolicy?.DefaultCheckIntervalSeconds is > 0)
        {
            source = "Tip";
            return typePolicy.DefaultCheckIntervalSeconds.Value;
        }

        if (schedulePlan is not null)
        {
            source = "Plan";
            return Math.Max(1, schedulePlan.IntervalMinutes) * 60;
        }

        source = "Global";
        return Math.Max(1, settings.AutoCheckIntervalMinutes) * 60;
    }

    private static int ResolvePingTimeoutMs(
        Device device,
        DeviceGroup? group,
        DeviceTypePolicy? typePolicy,
        SchedulePlan? schedulePlan,
        AppSettings settings,
        PingOptions? options,
        out string source)
    {
        if (device.PingTimeoutMs is > 0)
        {
            source = "Cihaz özel";
            return device.PingTimeoutMs.Value;
        }

        if (group?.DefaultPingTimeoutMs is > 0)
        {
            source = "Grup";
            return group.DefaultPingTimeoutMs.Value;
        }

        if (typePolicy?.DefaultPingTimeoutMs is > 0)
        {
            source = "Tip";
            return typePolicy.DefaultPingTimeoutMs.Value;
        }

        if (options is not null)
        {
            source = schedulePlan is not null ? "Plan" : "Global";
            return options.TimeoutMs;
        }

        source = "Global";
        return settings.PingTimeoutMs;
    }

    private static int ResolveRetryIntervalSeconds(
        Device device,
        DeviceGroup? group,
        DeviceTypePolicy? typePolicy,
        AppSettings settings,
        out string source)
    {
        if (device.FailureRetryIntervalSeconds > 0)
        {
            source = "Cihaz özel";
            return device.FailureRetryIntervalSeconds;
        }

        if (group?.DefaultFailureRetryIntervalSeconds is > 0)
        {
            source = "Grup";
            return group.DefaultFailureRetryIntervalSeconds.Value;
        }

        if (typePolicy?.DefaultFailureRetryIntervalSeconds is > 0)
        {
            source = "Tip";
            return typePolicy.DefaultFailureRetryIntervalSeconds.Value;
        }

        source = "Global";
        return settings.DefaultFailureRetryIntervalSeconds;
    }

    private static int ResolveRetryLimit(
        Device device,
        DeviceGroup? group,
        DeviceTypePolicy? typePolicy,
        AppSettings settings,
        out string source)
    {
        if (device.FailureRetryLimit > 0)
        {
            source = "Cihaz özel";
            return device.FailureRetryLimit;
        }

        if (group?.DefaultFailureRetryLimit is > 0)
        {
            source = "Grup";
            return group.DefaultFailureRetryLimit.Value;
        }

        if (typePolicy?.DefaultFailureRetryLimit is > 0)
        {
            source = "Tip";
            return typePolicy.DefaultFailureRetryLimit.Value;
        }

        source = "Global";
        return settings.DefaultFailureRetryLimit;
    }

    private static int ResolveFailureThreshold(
        Device device,
        DeviceGroup? group,
        DeviceTypePolicy? typePolicy,
        SchedulePlan? schedulePlan,
        AppSettings settings,
        PingOptions? options,
        out string source)
    {
        if (device.FailureThreshold > 0)
        {
            source = "Cihaz özel";
            return device.FailureThreshold;
        }

        if (group?.DefaultFailureThreshold is > 0)
        {
            source = "Grup";
            return group.DefaultFailureThreshold.Value;
        }

        if (typePolicy?.DefaultFailureThreshold is > 0)
        {
            source = "Tip";
            return typePolicy.DefaultFailureThreshold.Value;
        }

        if (options is not null)
        {
            source = schedulePlan is not null ? "Plan" : "Global";
            return options.FailureThreshold;
        }

        source = "Global";
        return settings.DefaultFailureThreshold;
    }
}
