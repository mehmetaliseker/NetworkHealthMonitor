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
