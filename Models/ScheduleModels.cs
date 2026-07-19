namespace NetworkHealthMonitor.Models;

public enum ScheduleMode
{
    FixedInterval = 0,
    TimesPerDay = 1,
    DailyTimes = 2,
    Weekly = 3
}

public enum ScheduleIntervalUnit
{
    Minutes = 0,
    Hours = 1,
    Days = 2,
    Weeks = 3
}

public enum MissedRunPolicy
{
    SkipMissed = 0,
    SingleCatchUp = 1,
    LimitedCatchUp = 2
}

public static class ScheduleModelExtensions
{
    public static string ToDisplayName(this ScheduleMode mode)
    {
        return mode switch
        {
            ScheduleMode.FixedInterval => "Sabit aralik",
            ScheduleMode.TimesPerDay => "Gunde belirli sayida",
            ScheduleMode.DailyTimes => "Gunluk ozel saatler",
            ScheduleMode.Weekly => "Haftalik plan",
            _ => "Sabit aralik"
        };
    }

    public static string ToDisplayName(this ScheduleIntervalUnit unit)
    {
        return unit switch
        {
            ScheduleIntervalUnit.Minutes => "Dakika",
            ScheduleIntervalUnit.Hours => "Saat",
            ScheduleIntervalUnit.Days => "Gun",
            ScheduleIntervalUnit.Weeks => "Hafta",
            _ => "Dakika"
        };
    }

    public static string ToStorageValue(this ScheduleMode mode)
    {
        return mode.ToString();
    }

    public static string ToStorageValue(this ScheduleIntervalUnit unit)
    {
        return unit.ToString();
    }

    public static string ToStorageValue(this MissedRunPolicy policy)
    {
        return policy.ToString();
    }

    public static ScheduleMode ScheduleModeFromStorage(string? value)
    {
        return Enum.TryParse<ScheduleMode>(value, true, out var parsed)
            ? parsed
            : ScheduleMode.FixedInterval;
    }

    public static ScheduleIntervalUnit ScheduleIntervalUnitFromStorage(string? value)
    {
        return Enum.TryParse<ScheduleIntervalUnit>(value, true, out var parsed)
            ? parsed
            : ScheduleIntervalUnit.Minutes;
    }

    public static MissedRunPolicy MissedRunPolicyFromStorage(string? value)
    {
        return Enum.TryParse<MissedRunPolicy>(value, true, out var parsed)
            ? parsed
            : MissedRunPolicy.SingleCatchUp;
    }

    public static string ToDisplayName(this MissedRunPolicy policy)
    {
        return policy switch
        {
            MissedRunPolicy.SkipMissed => "Kacirilanlari atla",
            MissedRunPolicy.SingleCatchUp => "Cihaz basina tek catch-up",
            MissedRunPolicy.LimitedCatchUp => "Sinirli catch-up",
            _ => "Cihaz basina tek catch-up"
        };
    }
}
