namespace NetworkHealthMonitor.Models;

public sealed record PingDeviceResult(
    Device Device,
    bool IsSuccess,
    long? LatencyMs,
    DateTime CheckedAt,
    string ResponseMessage,
    string ErrorMessage,
    DeviceStatus Status)
{
    public PingDeviceResult(
        Device device,
        bool isSuccess,
        long? latencyMs,
        DateTime checkedAt,
        string responseMessage,
        string errorMessage)
        : this(
            device,
            isSuccess,
            latencyMs,
            checkedAt,
            responseMessage,
            errorMessage,
            isSuccess ? DeviceStatus.Online : DeviceStatus.Warning)
    {
    }
}
