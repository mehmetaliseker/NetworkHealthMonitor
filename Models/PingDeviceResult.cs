namespace NetworkHealthMonitor.Models;

public sealed record PingDeviceResult(
    Device Device,
    bool IsSuccess,
    long? LatencyMs,
    DateTime CheckedAt,
    string ResponseMessage,
    string ErrorMessage)
{
    public DeviceStatus Status => IsSuccess ? DeviceStatus.Reachable : DeviceStatus.Unreachable;
}
