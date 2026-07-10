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
        plan.TimeoutMs = Math.Clamp(plan.TimeoutMs, AppSettings.MinPingTimeoutMs, AppSettings.MaxPingTimeoutMs);
        plan.MaxParallelism = Math.Clamp(plan.MaxParallelism, AppSettings.MinParallelPings, AppSettings.MaxParallelPingsLimit);
        plan.FailureThreshold = Math.Clamp(plan.FailureThreshold, AppSettings.MinFailureThreshold, AppSettings.MaxFailureThreshold);

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

        if (plan.TimeoutMs < AppSettings.MinPingTimeoutMs || plan.TimeoutMs > AppSettings.MaxPingTimeoutMs)
        {
            return OperationResult.Fail($"Ping timeout değeri {AppSettings.MinPingTimeoutMs} ile {AppSettings.MaxPingTimeoutMs} ms arasında olmalıdır.");
        }

        if (plan.MaxParallelism < AppSettings.MinParallelPings || plan.MaxParallelism > AppSettings.MaxParallelPingsLimit)
        {
            return OperationResult.Fail($"Maksimum paralel işlem sınırı {AppSettings.MinParallelPings} ile {AppSettings.MaxParallelPingsLimit} arasında olmalıdır.");
        }

        if (plan.FailureThreshold < AppSettings.MinFailureThreshold || plan.FailureThreshold > AppSettings.MaxFailureThreshold)
        {
            return OperationResult.Fail($"Başarısızlık eşiği {AppSettings.MinFailureThreshold} ile {AppSettings.MaxFailureThreshold} arasında olmalıdır.");
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
