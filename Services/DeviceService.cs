using NetworkHealthMonitor.Data;
using NetworkHealthMonitor.Models;

namespace NetworkHealthMonitor.Services;

public sealed class DeviceService : IDeviceService
{
    private const int MaxDeviceNameLength = 120;

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
            var deletedExisting = await _deviceRepository.GetByIpAsync(device.IpAddress, includeDeleted: true);
            if (deletedExisting is { IsDeleted: true })
            {
                device.Id = deletedExisting.Id;
                device.CreatedAt = deletedExisting.CreatedAt;
                device.UpdatedAt = now;
                device.IsDeleted = false;
                device.DeletedAtUtc = null;
                await _deviceRepository.UpdateAsync(device);
                return OperationResult.Ok("Cihaz eklendi.");
            }

            device.CreatedAt = now;
            device.UpdatedAt = now;
            await _deviceRepository.AddAsync(device);
            return OperationResult.Ok("Cihaz eklendi.");
        }

        var existing = await _deviceRepository.GetByIdAsync(device.Id, includeDeleted: true);
        if (existing is null)
        {
            return OperationResult.Fail("Güncellenecek cihaz bulunamadı.");
        }

        if (existing.IsDeleted)
        {
            return OperationResult.Fail("Silinmiş cihaz doğrudan düzenlenemez. Önce geri yükleyin.");
        }

        device.CreatedAt = existing.CreatedAt;
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

        var existing = await _deviceRepository.GetByIdAsync(device.Id, includeDeleted: true);
        if (existing is null || existing.IsDeleted)
        {
            return OperationResult.Fail("Silinecek cihaz bulunamadı.");
        }

        await _deviceRepository.DeleteAsync(device.Id);
        return OperationResult.Ok("Cihaz silindi.");
    }

    public async Task<OperationResult> RestoreAsync(Device device)
    {
        if (device.Id <= 0)
        {
            return OperationResult.Fail("Geri yüklenecek cihaz bulunamadı.");
        }

        await _deviceRepository.RestoreAsync(device.Id);
        return OperationResult.Ok("Cihaz geri yüklendi.");
    }

    public async Task<OperationResult> BulkDeleteAsync(IEnumerable<Device> devices)
    {
        var ids = devices.Where(device => device.Id > 0 && !device.IsDeleted).Select(device => device.Id).Distinct().ToList();
        if (ids.Count == 0)
        {
            return OperationResult.Fail("Silinecek cihaz bulunamadı.");
        }

        var affected = await _deviceRepository.BulkSoftDeleteAsync(ids);
        return OperationResult.Ok($"{affected} cihaz silindi.");
    }

    public async Task<OperationResult> BulkRestoreAsync(IEnumerable<Device> devices)
    {
        var ids = devices.Where(device => device.Id > 0 && device.IsDeleted).Select(device => device.Id).Distinct().ToList();
        if (ids.Count == 0)
        {
            return OperationResult.Fail("Geri yüklenecek cihaz bulunamadı.");
        }

        var affected = await _deviceRepository.BulkRestoreAsync(ids);
        return OperationResult.Ok($"{affected} cihaz geri yüklendi.");
    }

    public async Task<OperationResult> DeleteGroupDevicesAsync(DeviceGroup group, bool deleteEmptyGroup)
    {
        if (group.Id <= 0)
        {
            return OperationResult.Fail("Silinecek grup bulunamadı.");
        }

        var affected = await _deviceRepository.BulkSoftDeleteByGroupAsync(group.Id, deleteEmptyGroup);
        return OperationResult.Ok(deleteEmptyGroup
            ? $"{affected} cihaz silindi ve boş grup kaldırıldı."
            : $"{affected} cihaz silindi. Grup kaydı korundu.");
    }

    private async Task<OperationResult> ValidateAsync(Device device)
    {
        if (string.IsNullOrWhiteSpace(device.Name))
        {
            return OperationResult.Fail("Cihaz adı boş olamaz.");
        }

        if (device.Name.Trim().Length > MaxDeviceNameLength)
        {
            return OperationResult.Fail($"Cihaz adı en fazla {MaxDeviceNameLength} karakter olabilir.");
        }

        var addressValidation = IpAddressValidator.ValidateDeviceAddress(device.IpAddress);
        if (!addressValidation.IsValid)
        {
            return OperationResult.Fail(addressValidation.ErrorMessage);
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

        var existingByAddress = await _deviceRepository.GetByIpAsync(addressValidation.NormalizedAddress, includeDeleted: true);
        if (existingByAddress is not null
            && existingByAddress.Id != device.Id
            && (device.Id != 0 || !existingByAddress.IsDeleted))
        {
            return OperationResult.Fail("Bu IP adresi veya hostname zaten başka bir cihazda kayıtlı.");
        }

        device.Name = device.Name.Trim();
        device.IpAddress = addressValidation.NormalizedAddress;
        device.Location = device.Location.Trim();
        device.GroupName = device.GroupName.Trim();
        device.Description = device.Description.Trim();
        return OperationResult.Ok();
    }
}
