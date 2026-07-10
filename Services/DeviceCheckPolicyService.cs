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
        var normalIntervalSeconds = ResolveNormalIntervalSeconds(device, group, schedulePlan, settings);
        var retryIntervalSeconds = device.FailureRetryIntervalSeconds > 0
            ? device.FailureRetryIntervalSeconds
            : settings.DefaultFailureRetryIntervalSeconds;
        var retryLimit = device.FailureRetryLimit > 0
            ? device.FailureRetryLimit
            : settings.DefaultFailureRetryLimit;
        var failureThreshold = options?.FailureThreshold
            ?? schedulePlan?.FailureThreshold
            ?? settings.DefaultFailureThreshold;

        return new DeviceCheckPolicy(
            normalIntervalSeconds,
            retryIntervalSeconds,
            retryLimit,
            failureThreshold);
    }

    public bool IsDue(Device device, DeviceCheckPolicy policy, DateTime now)
    {
        if (!device.IsActive || !device.AutoCheckEnabled)
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

    private static int ResolveNormalIntervalSeconds(
        Device device,
        DeviceGroup? group,
        SchedulePlan? schedulePlan,
        AppSettings settings)
    {
        if (device.CheckIntervalSeconds > 0)
        {
            return device.CheckIntervalSeconds;
        }

        if (group?.DefaultCheckIntervalSeconds is > 0)
        {
            return group.DefaultCheckIntervalSeconds.Value;
        }

        if (schedulePlan is not null)
        {
            return Math.Max(1, schedulePlan.IntervalMinutes) * 60;
        }

        return Math.Max(1, settings.AutoCheckIntervalMinutes) * 60;
    }
}
