using NetworkHealthMonitor.Models;

namespace NetworkHealthMonitor.Services;

public sealed record DeviceConnectionTestResult(
    bool IsSuccess,
    string Message,
    long? LatencyMs,
    string TechnicalDetail);

public interface IDeviceConnectionTestService
{
    Task<DeviceConnectionTestResult> TestAsync(
        string address,
        int timeoutMilliseconds,
        CancellationToken cancellationToken = default);
}

public sealed class DeviceConnectionTestService : IDeviceConnectionTestService
{
    private readonly IPingService _pingService;

    public DeviceConnectionTestService(IPingService pingService)
    {
        _pingService = pingService;
    }

    public async Task<DeviceConnectionTestResult> TestAsync(
        string address,
        int timeoutMilliseconds,
        CancellationToken cancellationToken = default)
    {
        var validation = IpAddressValidator.ValidateDeviceAddress(address);
        if (!validation.IsValid)
        {
            return new DeviceConnectionTestResult(false, validation.ErrorMessage, null, string.Empty);
        }

        var options = new PingOptions(Math.Clamp(timeoutMilliseconds, AppSettings.MinPingTimeoutMs, AppSettings.MaxPingTimeoutMs), 1, 1);
        var device = new Device
        {
            Id = -1,
            Name = "Bağlantı testi",
            IpAddress = validation.NormalizedAddress,
            DeviceType = DeviceType.Other,
            IsActive = true,
            IsEnabled = true,
            LastStatus = DeviceStatus.Unknown,
            LastStableStatus = DeviceStatus.Unknown
        };

        var result = await _pingService.PingAsync(device, options, cancellationToken);
        if (result.IsSuccess)
        {
            return new DeviceConnectionTestResult(
                true,
                $"Bağlantı başarılı - {result.LatencyMs ?? 0} ms",
                result.LatencyMs,
                result.ResponseMessage);
        }

        var detail = string.IsNullOrWhiteSpace(result.ErrorMessage)
            ? result.ResponseMessage
            : result.ErrorMessage;
        return new DeviceConnectionTestResult(
            false,
            $"Cihaza ulaşılamadı. Timeout: {options.TimeoutMs} ms",
            null,
            detail);
    }
}
