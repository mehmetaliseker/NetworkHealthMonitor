using NetworkHealthMonitor.Data;
using NetworkHealthMonitor.Models;

namespace NetworkHealthMonitor.Services;

public sealed class DeviceGroupService : IDeviceGroupService
{
    private readonly DeviceGroupRepository _deviceGroupRepository;

    public DeviceGroupService(DeviceGroupRepository deviceGroupRepository)
    {
        _deviceGroupRepository = deviceGroupRepository;
    }

    public async Task<OperationResult> SaveAsync(DeviceGroup group)
    {
        if (string.IsNullOrWhiteSpace(group.Name))
        {
            return OperationResult.Fail("Grup adı boş olamaz.");
        }

        if (await _deviceGroupRepository.ExistsByNameAsync(group.Name.Trim(), group.Id == 0 ? null : group.Id))
        {
            return OperationResult.Fail("Bu grup adı zaten kullanılıyor.");
        }

        if (group.DefaultCheckIntervalSeconds.HasValue
            && (group.DefaultCheckIntervalSeconds.Value < AppSettings.MinDeviceCheckIntervalSeconds
                || group.DefaultCheckIntervalSeconds.Value > AppSettings.MaxDeviceCheckIntervalSeconds))
        {
            return OperationResult.Fail($"Grup varsayılan kontrol aralığı {AppSettings.MinDeviceCheckIntervalSeconds} saniye ile 24 saat arasında olmalıdır.");
        }

        if (group.DefaultPingTimeoutMs.HasValue
            && (group.DefaultPingTimeoutMs.Value < AppSettings.MinPingTimeoutMs
                || group.DefaultPingTimeoutMs.Value > AppSettings.MaxPingTimeoutMs))
        {
            return OperationResult.Fail($"Grup varsayılan timeout değeri {AppSettings.MinPingTimeoutMs} ile {AppSettings.MaxPingTimeoutMs} ms arasında olmalıdır.");
        }

        if (group.DefaultFailureRetryIntervalSeconds.HasValue
            && (group.DefaultFailureRetryIntervalSeconds.Value < AppSettings.MinFailureRetryIntervalSeconds
                || group.DefaultFailureRetryIntervalSeconds.Value > AppSettings.MaxFailureRetryIntervalSeconds))
        {
            return OperationResult.Fail($"Grup retry aralığı {AppSettings.MinFailureRetryIntervalSeconds} saniye ile {AppSettings.MaxFailureRetryIntervalSeconds} saniye arasında olmalıdır.");
        }

        if (group.DefaultFailureRetryLimit.HasValue
            && (group.DefaultFailureRetryLimit.Value < AppSettings.MinFailureRetryLimit
                || group.DefaultFailureRetryLimit.Value > AppSettings.MaxFailureRetryLimit))
        {
            return OperationResult.Fail($"Grup retry limiti {AppSettings.MinFailureRetryLimit} ile {AppSettings.MaxFailureRetryLimit} arasında olmalıdır.");
        }

        if (group.DefaultFailureThreshold.HasValue
            && (group.DefaultFailureThreshold.Value < AppSettings.MinFailureThreshold
                || group.DefaultFailureThreshold.Value > AppSettings.MaxFailureThreshold))
        {
            return OperationResult.Fail($"Grup başarısızlık eşiği {AppSettings.MinFailureThreshold} ile {AppSettings.MaxFailureThreshold} arasında olmalıdır.");
        }

        group.Name = group.Name.Trim();
        if (group.Id == 0)
        {
            await _deviceGroupRepository.AddAsync(group);
            return OperationResult.Ok("Grup oluşturuldu.");
        }

        await _deviceGroupRepository.UpdateAsync(group);
        return OperationResult.Ok("Grup güncellendi.");
    }

    public async Task<OperationResult> DeleteAsync(DeviceGroup group)
    {
        if (group.Id <= 0)
        {
            return OperationResult.Fail("Silinecek grup bulunamadı.");
        }

        await _deviceGroupRepository.DeleteAsync(group.Id);
        return OperationResult.Ok("Grup silindi. Bu gruptaki cihazlar korunarak grup bağlantıları kaldırıldı.");
    }
}
