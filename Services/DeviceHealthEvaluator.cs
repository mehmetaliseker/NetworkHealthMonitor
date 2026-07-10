using NetworkHealthMonitor.Models;

namespace NetworkHealthMonitor.Services;

public sealed class DeviceHealthEvaluator : IDeviceHealthEvaluator
{
    public DeviceStatus Evaluate(Device device, PingDeviceResult result, DeviceCheckPolicy policy)
    {
        if (result.IsSuccess)
        {
            return DeviceStatus.Online;
        }

        var nextFailureCount = device.ConsecutiveFailures + 1;
        if (nextFailureCount < policy.FailureThreshold)
        {
            return LooksLikeNoReply(result)
                ? DeviceStatus.PingBlockedOrNoReply
                : DeviceStatus.Warning;
        }

        if (nextFailureCount < policy.OfflineFailureCount)
        {
            return DeviceStatus.UnderWatch;
        }

        return DeviceStatus.Offline;
    }

    private static bool LooksLikeNoReply(PingDeviceResult result)
    {
        var text = $"{result.ResponseMessage} {result.ErrorMessage}".ToLowerInvariant();
        return text.Contains("zaman aşımı", StringComparison.Ordinal)
            || text.Contains("timeout", StringComparison.Ordinal)
            || text.Contains("yanıt alınamadı", StringComparison.Ordinal)
            || text.Contains("no reply", StringComparison.Ordinal)
            || text.Contains("timed out", StringComparison.Ordinal);
    }
}
