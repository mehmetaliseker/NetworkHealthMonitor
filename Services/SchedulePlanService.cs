using NetworkHealthMonitor.Data;
using NetworkHealthMonitor.Models;

namespace NetworkHealthMonitor.Services;

public sealed class SchedulePlanService : ISchedulePlanService
{
    private readonly SchedulePlanRepository _schedulePlanRepository;

    public SchedulePlanService(SchedulePlanRepository schedulePlanRepository)
    {
        _schedulePlanRepository = schedulePlanRepository;
    }

    public async Task<OperationResult> SaveAsync(SchedulePlan plan)
    {
        var validation = await ValidateAsync(plan);
        if (!validation.Success)
        {
            return validation;
        }

        plan.Name = plan.Name.Trim();
        plan.TargetValue = NormalizeTargetValue(plan);
        plan.IntervalMinutes = Math.Max(1, plan.IntervalMinutes);
        plan.TimeoutMs = Math.Clamp(plan.TimeoutMs, 250, 10000);
        plan.MaxParallelism = Math.Clamp(plan.MaxParallelism, 1, 128);
        plan.FailureThreshold = Math.Clamp(plan.FailureThreshold, 1, 20);

        if (plan.Id == 0)
        {
            await _schedulePlanRepository.AddAsync(plan);
            return OperationResult.Ok("Otomatik kontrol planı oluşturuldu.");
        }

        await _schedulePlanRepository.UpdateAsync(plan);
        return OperationResult.Ok("Otomatik kontrol planı güncellendi.");
    }

    public async Task<OperationResult> DeleteAsync(SchedulePlan plan)
    {
        if (plan.Id <= 0)
        {
            return OperationResult.Fail("Silinecek plan bulunamadı.");
        }

        await _schedulePlanRepository.DeleteAsync(plan.Id);
        return OperationResult.Ok("Otomatik kontrol planı silindi.");
    }

    private async Task<OperationResult> ValidateAsync(SchedulePlan plan)
    {
        if (string.IsNullOrWhiteSpace(plan.Name))
        {
            return OperationResult.Fail("Plan adı boş olamaz.");
        }

        if (await _schedulePlanRepository.ExistsByNameAsync(plan.Name.Trim(), plan.Id == 0 ? null : plan.Id))
        {
            return OperationResult.Fail("Bu plan adı zaten kullanılıyor.");
        }

        if (plan.IntervalMinutes < 1)
        {
            return OperationResult.Fail("Kontrol sıklığı en az 1 dakika olmalıdır.");
        }

        if (plan.TimeoutMs < 250 || plan.TimeoutMs > 10000)
        {
            return OperationResult.Fail("Ping timeout değeri 250 ile 10000 ms arasında olmalıdır.");
        }

        if (plan.MaxParallelism < 1 || plan.MaxParallelism > 128)
        {
            return OperationResult.Fail("Maksimum paralel işlem sınırı 1 ile 128 arasında olmalıdır.");
        }

        if (plan.FailureThreshold < 1 || plan.FailureThreshold > 20)
        {
            return OperationResult.Fail("Başarısızlık eşiği 1 ile 20 arasında olmalıdır.");
        }

        if (plan.TargetType is SchedulePlanTargetType.Device or SchedulePlanTargetType.DeviceType or SchedulePlanTargetType.DeviceGroup
            && string.IsNullOrWhiteSpace(plan.TargetValue))
        {
            return OperationResult.Fail("Bu plan tipi için hedef seçimi zorunludur.");
        }

        return OperationResult.Ok();
    }

    private static string NormalizeTargetValue(SchedulePlan plan)
    {
        return plan.TargetType is SchedulePlanTargetType.AllDevices or SchedulePlanTargetType.CriticalDevices
            ? string.Empty
            : plan.TargetValue.Trim();
    }
}
