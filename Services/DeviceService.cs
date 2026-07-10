using NetworkHealthMonitor.Data;
using NetworkHealthMonitor.Models;

namespace NetworkHealthMonitor.Services;

public sealed class DeviceService : IDeviceService
{
    private readonly DeviceRepository _deviceRepository;

    public DeviceService(DeviceRepository deviceRepository)
    {
        _deviceRepository = deviceRepository;
    }

    public async Task<OperationResult> SaveAsync(Device device)
    {
        var validation = await ValidateAsync(device);
        if (!validation.Success)
        {
            return validation;
        }

        var now = DateTime.Now;
        if (device.Id == 0)
        {
            device.CreatedAt = now;
            device.UpdatedAt = now;
            await _deviceRepository.AddAsync(device);
            return OperationResult.Ok("Cihaz eklendi.");
        }

        device.UpdatedAt = now;
        await _deviceRepository.UpdateAsync(device);
        return OperationResult.Ok("Cihaz güncellendi.");
    }

    public async Task<OperationResult> DeleteAsync(Device device)
    {
        if (device.Id <= 0)
        {
            return OperationResult.Fail("Silinecek cihaz bulunamadı.");
        }

        await _deviceRepository.DeleteAsync(device.Id);
        return OperationResult.Ok("Cihaz silindi.");
    }

    private async Task<OperationResult> ValidateAsync(Device device)
    {
        if (string.IsNullOrWhiteSpace(device.Name))
        {
            return OperationResult.Fail("Cihaz adı boş olamaz.");
        }

        if (string.IsNullOrWhiteSpace(device.IpAddress))
        {
            return OperationResult.Fail("IP adresi boş olamaz.");
        }

        if (!IpAddressValidator.IsValidIpv4(device.IpAddress.Trim()))
        {
            return OperationResult.Fail("IP adresi geçerli IPv4 formatında olmalıdır.");
        }

        if (!Enum.IsDefined(device.DeviceType))
        {
            return OperationResult.Fail("Cihaz tipi seçilmeden kayıt yapılamaz.");
        }

        if (device.CheckIntervalSeconds != 0
            && (device.CheckIntervalSeconds < AppSettings.MinDeviceCheckIntervalSeconds
                || device.CheckIntervalSeconds > AppSettings.MaxDeviceCheckIntervalSeconds))
        {
            return OperationResult.Fail($"Normal kontrol aralığı 0 veya {AppSettings.MinDeviceCheckIntervalSeconds} saniye ile 24 saat arasında olmalıdır.");
        }

        if (device.PingTimeoutMs.HasValue
            && (device.PingTimeoutMs.Value < AppSettings.MinPingTimeoutMs
                || device.PingTimeoutMs.Value > AppSettings.MaxPingTimeoutMs))
        {
            return OperationResult.Fail($"Cihaz özel ping timeout değeri {AppSettings.MinPingTimeoutMs} ile {AppSettings.MaxPingTimeoutMs} ms arasında olmalıdır.");
        }

        if (device.FailureRetryIntervalSeconds != 0
            && (device.FailureRetryIntervalSeconds < AppSettings.MinFailureRetryIntervalSeconds
                || device.FailureRetryIntervalSeconds > AppSettings.MaxFailureRetryIntervalSeconds))
        {
            return OperationResult.Fail($"Hızlı tekrar aralığı 0 veya {AppSettings.MinFailureRetryIntervalSeconds} saniye ile {AppSettings.MaxFailureRetryIntervalSeconds} saniye arasında olmalıdır.");
        }

        if (device.FailureRetryLimit != 0
            && (device.FailureRetryLimit < AppSettings.MinFailureRetryLimit
                || device.FailureRetryLimit > AppSettings.MaxFailureRetryLimit))
        {
            return OperationResult.Fail($"Hızlı tekrar limiti 0 veya {AppSettings.MinFailureRetryLimit} ile {AppSettings.MaxFailureRetryLimit} arasında olmalıdır.");
        }

        if (device.FailureThreshold != 0
            && (device.FailureThreshold < AppSettings.MinFailureThreshold
                || device.FailureThreshold > AppSettings.MaxFailureThreshold))
        {
            return OperationResult.Fail($"Başarısızlık eşiği 0 veya {AppSettings.MinFailureThreshold} ile {AppSettings.MaxFailureThreshold} arasında olmalıdır.");
        }

        if (await _deviceRepository.ExistsByIpAsync(device.IpAddress.Trim(), device.Id == 0 ? null : device.Id))
        {
            return OperationResult.Fail("Bu IP adresi zaten başka bir cihazda kayıtlı.");
        }

        device.Name = device.Name.Trim();
        device.IpAddress = device.IpAddress.Trim();
        device.Location = device.Location.Trim();
        device.GroupName = device.GroupName.Trim();
        return OperationResult.Ok();
    }
}
