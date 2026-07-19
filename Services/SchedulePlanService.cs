using NetworkHealthMonitor.Data;
using NetworkHealthMonitor.Models;

namespace NetworkHealthMonitor.Services;

public sealed class SchedulePlanService : ISchedulePlanService
{
    private readonly SchedulePlanRepository _schedulePlanRepository;
    private readonly ScheduleTimingService _scheduleTimingService;

    public SchedulePlanService(
        SchedulePlanRepository schedulePlanRepository,
        ScheduleTimingService? scheduleTimingService = null)
    {
        _schedulePlanRepository = schedulePlanRepository;
        _scheduleTimingService = scheduleTimingService ?? new ScheduleTimingService();
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
        NormalizeSchedulerFields(plan);
        plan.TimeoutMs = Math.Clamp(plan.TimeoutMs, AppSettings.MinPingTimeoutMs, AppSettings.MaxPingTimeoutMs);
        plan.MaxParallelism = Math.Clamp(plan.MaxParallelism, AppSettings.MinParallelPings, AppSettings.MaxParallelPingsLimit);
        plan.FailureThreshold = Math.Clamp(plan.FailureThreshold, AppSettings.MinFailureThreshold, AppSettings.MaxFailureThreshold);

        if (plan.Id == 0)
        {
            await _schedulePlanRepository.AddAsync(plan);
            return OperationResult.Ok("Otomatik kontrol plani olusturuldu.");
        }

        await _schedulePlanRepository.UpdateAsync(plan);
        return OperationResult.Ok("Otomatik kontrol plani guncellendi.");
    }

    public async Task<OperationResult> DeleteAsync(SchedulePlan plan)
    {
        if (plan.Id <= 0)
        {
            return OperationResult.Fail("Silinecek plan bulunamadi.");
        }

        await _schedulePlanRepository.DeleteAsync(plan.Id);
        return OperationResult.Ok("Otomatik kontrol plani silindi.");
    }

    private async Task<OperationResult> ValidateAsync(SchedulePlan plan)
    {
        if (string.IsNullOrWhiteSpace(plan.Name))
        {
            return OperationResult.Fail("Plan adi bos olamaz.");
        }

        if (await _schedulePlanRepository.ExistsByNameAsync(plan.Name.Trim(), plan.Id == 0 ? null : plan.Id))
        {
            return OperationResult.Fail("Bu plan adi zaten kullaniliyor.");
        }

        var scheduleValidation = _scheduleTimingService.Validate(plan);
        if (!scheduleValidation.Success)
        {
            return scheduleValidation;
        }

        if (plan.TimeoutMs < AppSettings.MinPingTimeoutMs || plan.TimeoutMs > AppSettings.MaxPingTimeoutMs)
        {
            return OperationResult.Fail($"Ping timeout degeri {AppSettings.MinPingTimeoutMs} ile {AppSettings.MaxPingTimeoutMs} ms arasinda olmalidir.");
        }

        if (plan.MaxParallelism < AppSettings.MinParallelPings || plan.MaxParallelism > AppSettings.MaxParallelPingsLimit)
        {
            return OperationResult.Fail($"Maksimum paralel islem siniri {AppSettings.MinParallelPings} ile {AppSettings.MaxParallelPingsLimit} arasinda olmalidir.");
        }

        if (plan.FailureThreshold < AppSettings.MinFailureThreshold || plan.FailureThreshold > AppSettings.MaxFailureThreshold)
        {
            return OperationResult.Fail($"Basarisizlik esigi {AppSettings.MinFailureThreshold} ile {AppSettings.MaxFailureThreshold} arasinda olmalidir.");
        }

        if (plan.TargetType is SchedulePlanTargetType.Device or SchedulePlanTargetType.DeviceType or SchedulePlanTargetType.DeviceGroup
            && string.IsNullOrWhiteSpace(plan.TargetValue))
        {
            return OperationResult.Fail("Bu plan tipi icin hedef secimi zorunludur.");
        }

        return OperationResult.Ok();
    }

    private static string NormalizeTargetValue(SchedulePlan plan)
    {
        return plan.TargetType is SchedulePlanTargetType.AllDevices or SchedulePlanTargetType.CriticalDevices
            ? string.Empty
            : plan.TargetValue.Trim();
    }

    private static void NormalizeSchedulerFields(SchedulePlan plan)
    {
        plan.TimeZoneId = string.IsNullOrWhiteSpace(plan.TimeZoneId) ? TimeZoneInfo.Local.Id : plan.TimeZoneId.Trim();
        plan.IntervalMinutes = ScheduleTimingService.GetLegacyIntervalMinutes(plan);
        plan.DailyTimes = ScheduleTimingService.NormalizeTimeList(plan.DailyTimes);
        plan.SelectedWeekDays = ScheduleTimingService.NormalizeWeekDays(plan.SelectedWeekDays);
        plan.TimesPerDay = Math.Clamp(plan.TimesPerDay, 0, 48);
        plan.ConfirmationRetryCount = Math.Clamp(plan.ConfirmationRetryCount, AppSettings.MinConfirmationRetryCount, AppSettings.MaxConfirmationRetryCount);
        plan.ConfirmationRetryIntervalSeconds = Math.Clamp(plan.ConfirmationRetryIntervalSeconds, AppSettings.MinConfirmationRetryIntervalSeconds, AppSettings.MaxConfirmationRetryIntervalSeconds);
        plan.OfflineRecheckIntervalSeconds = Math.Clamp(plan.OfflineRecheckIntervalSeconds, AppSettings.MinOfflineRecheckIntervalSeconds, AppSettings.MaxOfflineRecheckIntervalSeconds);
    }
}
