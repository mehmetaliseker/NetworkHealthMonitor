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
