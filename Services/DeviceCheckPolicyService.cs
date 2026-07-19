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
        var retryIntervalSeconds = ResolveRetryIntervalSeconds(device, group, typePolicy, schedulePlan, settings, out var retryIntervalSource);
        var retryLimit = ResolveRetryLimit(device, group, typePolicy, schedulePlan, settings, out var retryLimitSource);
        var offlineRecheckIntervalSeconds = ResolveOfflineRecheckIntervalSeconds(device, group, typePolicy, schedulePlan, out var offlineRecheckSource);
        var failureThreshold = ResolveFailureThreshold(device, group, typePolicy, schedulePlan, settings, options, out var thresholdSource);

        return new DeviceCheckPolicy(
            autoCheckEnabled,
            normalIntervalSeconds,
            pingTimeoutMs,
            retryIntervalSeconds,
            retryLimit,
            offlineRecheckIntervalSeconds,
            failureThreshold,
            $"Otomatik: {autoSource}, Aralik: {intervalSource}, Zaman asimi: {timeoutSource}, Hizli retry: {retryIntervalSource}/{retryLimitSource}, Erisilemeyen kontrol: {offlineRecheckSource}, Esik: {thresholdSource}");
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

        var intervalSeconds = ShouldUseConfirmationRetry(device, policy)
            ? policy.FailureRetryIntervalSeconds
            : ShouldUseOfflineRecheck(device, policy)
                ? policy.OfflineRecheckIntervalSeconds
                : policy.NormalIntervalSeconds;

        return lastCheckAt.Value.AddSeconds(intervalSeconds);
    }

    private static bool ShouldUseConfirmationRetry(Device device, DeviceCheckPolicy policy)
    {
        return policy.HasFailureRetryRemaining(device.ConsecutiveFailures)
            && device.LastStatus.IsFailureObservation();
    }

    private static bool ShouldUseOfflineRecheck(Device device, DeviceCheckPolicy policy)
    {
        return device.LastStatus == DeviceStatus.Offline
            || device.ConsecutiveFailures >= policy.OfflineFailureCount && device.LastStatus.IsFailureObservation();
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
            source = "Cihaz ozel";
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
            source = "Cihaz ozel";
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
            return ScheduleTimingService.GetLegacyIntervalMinutes(schedulePlan) * 60;
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
            source = "Cihaz ozel";
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
        SchedulePlan? schedulePlan,
        AppSettings settings,
        out string source)
    {
        if (device.FailureRetryIntervalSeconds > 0)
        {
            source = "Cihaz ozel";
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

        if (schedulePlan is not null)
        {
            source = "Plan";
            return schedulePlan.FailureRetryEnabled
                ? schedulePlan.ConfirmationRetryIntervalSeconds
                : AppSettings.MinConfirmationRetryIntervalSeconds;
        }

        source = "Global";
        return settings.DefaultFailureRetryIntervalSeconds;
    }

    private static int ResolveRetryLimit(
        Device device,
        DeviceGroup? group,
        DeviceTypePolicy? typePolicy,
        SchedulePlan? schedulePlan,
        AppSettings settings,
        out string source)
    {
        if (device.FailureRetryLimit > 0)
        {
            source = "Cihaz ozel";
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

        if (schedulePlan is not null)
        {
            source = "Plan";
            return schedulePlan.FailureRetryEnabled ? schedulePlan.ConfirmationRetryCount : 0;
        }

        source = "Global";
        return settings.DefaultFailureRetryLimit;
    }

    private static int ResolveOfflineRecheckIntervalSeconds(
        Device device,
        DeviceGroup? group,
        DeviceTypePolicy? typePolicy,
        SchedulePlan? schedulePlan,
        out string source)
    {
        if (device.FailureRetryIntervalSeconds > 0)
        {
            source = "Cihaz ozel";
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

        if (schedulePlan is not null)
        {
            source = "Plan";
            return schedulePlan.OfflineRecheckIntervalSeconds;
        }

        source = "Global";
        return AppSettings.DefaultOfflineRecheckIntervalSeconds;
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
            source = "Cihaz ozel";
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
