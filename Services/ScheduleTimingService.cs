using NetworkHealthMonitor.Models;

namespace NetworkHealthMonitor.Services;

public sealed class ScheduleTimingService
{
    private static readonly char[] ListSeparators = new[] { ',', ';', '\n', '\r', '|' };

    public bool IsDue(SchedulePlan plan, DateTime now)
    {
        var nowUtc = ToUtc(now);
        return plan.IsActive && GetEffectiveNextRun(plan, nowUtc) <= nowUtc;
    }

    public DateTime GetEffectiveNextRun(SchedulePlan plan, DateTime now)
    {
        var nowUtc = ToUtc(now);
        if (plan.NextRunAt.HasValue)
        {
            return ToUtc(plan.NextRunAt.Value);
        }

        if (plan.LastRunAt.HasValue)
        {
            var afterLastRun = CalculateNextRunAfterExecution(plan, plan.LastRunAt.Value);
            return afterLastRun <= nowUtc && plan.MissedRunPolicy != MissedRunPolicy.LimitedCatchUp
                ? nowUtc
                : afterLastRun;
        }

        return nowUtc;
    }

    public DateTime CalculateNextRunAfterExecution(SchedulePlan plan, DateTime executionTime)
    {
        var executionUtc = ToUtc(executionTime);
        return plan.ScheduleMode == ScheduleMode.FixedInterval
            ? executionUtc.Add(GetFixedInterval(plan))
            : GetNextOccurrences(plan, executionUtc, 1).FirstOrDefault(executionUtc.Add(GetFixedInterval(plan)));
    }

    public IReadOnlyList<DateTime> GetNextOccurrences(SchedulePlan plan, DateTime fromUtc, int count)
    {
        if (count <= 0)
        {
            return Array.Empty<DateTime>();
        }

        var normalizedFromUtc = ToUtc(fromUtc);
        return plan.ScheduleMode == ScheduleMode.FixedInterval
            ? GetFixedIntervalOccurrences(plan, normalizedFromUtc, count)
            : GetCalendarOccurrences(plan, normalizedFromUtc, count);
    }

    public OperationResult Validate(SchedulePlan plan)
    {
        if (plan.ScheduleMode == ScheduleMode.FixedInterval)
        {
            if (plan.IntervalValue <= 0)
            {
                return OperationResult.Fail("Normal kontrol araligi sifir veya negatif olamaz.");
            }

            var interval = GetFixedInterval(plan);
            if (interval < TimeSpan.FromMinutes(1) || interval > TimeSpan.FromDays(365))
            {
                return OperationResult.Fail("Normal kontrol aralığı 1 dakika ile 365 gün arasında olmalıdır.");
            }
        }
        else if (plan.ScheduleMode == ScheduleMode.TimesPerDay)
        {
            if (plan.TimesPerDay is < 1 or > 48)
            {
                return OperationResult.Fail("Günlük kontrol sayısı 1 ile 48 arasında olmalıdır.");
            }

            if (!string.IsNullOrWhiteSpace(plan.DailyTimes))
            {
                var times = ParseTimeList(plan.DailyTimes);
                if (times.Count != DistinctTimes(times).Count)
                {
                    return OperationResult.Fail("Aynı saat aynı günlük plan içinde iki kez eklenemez.");
                }
            }
        }
        else if (plan.ScheduleMode == ScheduleMode.DailyTimes)
        {
            var times = ParseTimeList(plan.DailyTimes);
            if (times.Count is < 1 or > 48)
            {
                return OperationResult.Fail("Günlük özel saat listesi 1 ile 48 saat arasında olmalıdır.");
            }

            if (times.Count != DistinctTimes(times).Count)
            {
                return OperationResult.Fail("Aynı saat aynı günlük plan içinde iki kez eklenemez.");
            }
        }
        else if (plan.ScheduleMode == ScheduleMode.Weekly)
        {
            var days = ParseWeekDays(plan.SelectedWeekDays);
            if (days.Count == 0)
            {
                return OperationResult.Fail("Haftalık plan için en az bir gün seçilmelidir.");
            }

            var times = ParseTimeList(plan.DailyTimes);
            if (times.Count is < 1 or > 48)
            {
                return OperationResult.Fail("Haftalık plan için en az bir, en fazla 48 saat tanımlanmalıdır.");
            }

            if (times.Count != DistinctTimes(times).Count)
            {
                return OperationResult.Fail("Aynı saat aynı haftalık plan içinde iki kez eklenemez.");
            }
        }

        if (plan.ConfirmationRetryCount is < AppSettings.MinConfirmationRetryCount or > AppSettings.MaxConfirmationRetryCount)
        {
            return OperationResult.Fail("Hızlı retry sayısı 0 ile 10 arasında olmalıdır.");
        }

        if (plan.ConfirmationRetryIntervalSeconds is < AppSettings.MinConfirmationRetryIntervalSeconds or > AppSettings.MaxConfirmationRetryIntervalSeconds)
        {
            return OperationResult.Fail("Hızlı retry aralığı 10 saniye ile 30 dakika arasında olmalıdır.");
        }

        if (plan.OfflineRecheckIntervalSeconds is < AppSettings.MinOfflineRecheckIntervalSeconds or > AppSettings.MaxOfflineRecheckIntervalSeconds)
        {
            return OperationResult.Fail("Erişilemeyen cihaz kontrol aralığı 1 dakika ile 30 gün arasında olmalıdır.");
        }

        return OperationResult.Ok();
    }

    public static int GetLegacyIntervalMinutes(SchedulePlan plan)
    {
        if (plan.ScheduleMode == ScheduleMode.FixedInterval)
        {
            return (int)Math.Clamp(GetFixedInterval(plan).TotalMinutes, 1, AppSettings.MaxSchedulePlanIntervalMinutes);
        }

        if (plan.ScheduleMode == ScheduleMode.TimesPerDay && plan.TimesPerDay > 0)
        {
            return Math.Max(1, (int)Math.Round(TimeSpan.FromDays(1).TotalMinutes / plan.TimesPerDay));
        }

        return Math.Max(1, plan.IntervalMinutes);
    }

    public static string NormalizeTimeList(string value)
    {
        return string.Join(";", DistinctTimes(ParseTimeList(value)).Select(time => time.ToString("HH\\:mm")));
    }

    public static string NormalizeWeekDays(string value)
    {
        return string.Join(",", ParseWeekDays(value).OrderBy(day => ((int)day + 6) % 7).Select(day => day.ToString()));
    }

    private static IReadOnlyList<DateTime> GetFixedIntervalOccurrences(SchedulePlan plan, DateTime fromUtc, int count)
    {
        var interval = GetFixedInterval(plan);
        var next = plan.NextRunAt.HasValue ? ToUtc(plan.NextRunAt.Value) : fromUtc;
        if (plan.LastRunAt.HasValue && !plan.NextRunAt.HasValue)
        {
            next = ToUtc(plan.LastRunAt.Value).Add(interval);
        }

        while (next <= fromUtc)
        {
            next = next.Add(interval);
        }

        var occurrences = new List<DateTime>(count);
        for (var i = 0; i < count; i++)
        {
            occurrences.Add(next);
            next = next.Add(interval);
        }

        return occurrences;
    }

    private static IReadOnlyList<DateTime> GetCalendarOccurrences(SchedulePlan plan, DateTime fromUtc, int count)
    {
        var timeZone = ResolveTimeZone(plan.TimeZoneId);
        var fromLocal = TimeZoneInfo.ConvertTimeFromUtc(fromUtc, timeZone);
        var times = ResolveDailyTimes(plan);
        var selectedDays = plan.ScheduleMode == ScheduleMode.Weekly
            ? ParseWeekDays(plan.SelectedWeekDays)
            : Enum.GetValues<DayOfWeek>().ToHashSet();
        var occurrences = new List<DateTime>(count);

        for (var dayOffset = 0; occurrences.Count < count && dayOffset <= 370; dayOffset++)
        {
            var localDate = fromLocal.Date.AddDays(dayOffset);
            if (!selectedDays.Contains(localDate.DayOfWeek))
            {
                continue;
            }

            foreach (var time in times)
            {
                var localCandidate = DateTime.SpecifyKind(localDate.Add(time), DateTimeKind.Unspecified);
                if (localCandidate <= fromLocal)
                {
                    continue;
                }

                var candidateUtc = ConvertLocalToUtcSafely(localCandidate, timeZone);
                if (candidateUtc <= fromUtc || occurrences.Contains(candidateUtc))
                {
                    continue;
                }

                occurrences.Add(candidateUtc);
                if (occurrences.Count >= count)
                {
                    break;
                }
            }
        }

        return occurrences.OrderBy(value => value).Take(count).ToList();
    }

    private static IReadOnlyList<TimeSpan> ResolveDailyTimes(SchedulePlan plan)
    {
        if (plan.ScheduleMode == ScheduleMode.TimesPerDay && string.IsNullOrWhiteSpace(plan.DailyTimes))
        {
            var count = Math.Clamp(plan.TimesPerDay, 1, 48);
            var stepTicks = TimeSpan.TicksPerDay / count;
            return Enumerable.Range(0, count)
                .Select(index => TimeSpan.FromTicks(stepTicks * index))
                .ToList();
        }

        return DistinctTimes(ParseTimeList(plan.DailyTimes));
    }

    private static IReadOnlyList<TimeSpan> ParseTimeList(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return Array.Empty<TimeSpan>();
        }

        var result = new List<TimeSpan>();
        foreach (var token in value.Split(ListSeparators, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            if (TimeSpan.TryParse(token, out var time) && time >= TimeSpan.Zero && time < TimeSpan.FromDays(1))
            {
                result.Add(new TimeSpan(time.Hours, time.Minutes, 0));
            }
        }

        return result;
    }

    private static IReadOnlyList<TimeSpan> DistinctTimes(IEnumerable<TimeSpan> values)
    {
        return values
            .Select(value => new TimeSpan(value.Hours, value.Minutes, 0))
            .Distinct()
            .OrderBy(value => value)
            .ToList();
    }

    private static HashSet<DayOfWeek> ParseWeekDays(string value)
    {
        var days = new HashSet<DayOfWeek>();
        if (string.IsNullOrWhiteSpace(value))
        {
            return days;
        }

        foreach (var token in value.Split(ListSeparators, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            if (int.TryParse(token, out var numeric) && numeric is >= 0 and <= 6)
            {
                days.Add((DayOfWeek)numeric);
                continue;
            }

            if (Enum.TryParse<DayOfWeek>(token, ignoreCase: true, out var parsed))
            {
                days.Add(parsed);
                continue;
            }

            if (TryParseTurkishWeekDay(token, out var turkishDay))
            {
                days.Add(turkishDay);
            }
        }

        return days;
    }

    private static bool TryParseTurkishWeekDay(string value, out DayOfWeek day)
    {
        day = value.Trim().ToLowerInvariant() switch
        {
            "pazartesi" => DayOfWeek.Monday,
            "sali" or "salı" => DayOfWeek.Tuesday,
            "carsamba" or "çarşamba" => DayOfWeek.Wednesday,
            "persembe" or "perşembe" => DayOfWeek.Thursday,
            "cuma" => DayOfWeek.Friday,
            "cumartesi" => DayOfWeek.Saturday,
            "pazar" => DayOfWeek.Sunday,
            _ => (DayOfWeek)(-1)
        };
        return day != (DayOfWeek)(-1);
    }

    private static TimeSpan GetFixedInterval(SchedulePlan plan)
    {
        var value = Math.Max(1, plan.IntervalValue);
        return plan.IntervalUnit switch
        {
            ScheduleIntervalUnit.Hours => TimeSpan.FromHours(value),
            ScheduleIntervalUnit.Days => TimeSpan.FromDays(value),
            ScheduleIntervalUnit.Weeks => TimeSpan.FromDays(value * 7),
            _ => TimeSpan.FromMinutes(value)
        };
    }

    private static TimeZoneInfo ResolveTimeZone(string timeZoneId)
    {
        if (string.IsNullOrWhiteSpace(timeZoneId))
        {
            return TimeZoneInfo.Local;
        }

        try
        {
            return TimeZoneInfo.FindSystemTimeZoneById(timeZoneId);
        }
        catch
        {
            return TimeZoneInfo.Local;
        }
    }

    private static DateTime ConvertLocalToUtcSafely(DateTime localCandidate, TimeZoneInfo timeZone)
    {
        var candidate = localCandidate;
        while (timeZone.IsInvalidTime(candidate))
        {
            candidate = candidate.AddMinutes(1);
        }

        return TimeZoneInfo.ConvertTimeToUtc(candidate, timeZone);
    }

    private static DateTime ToUtc(DateTime value)
    {
        return value.Kind switch
        {
            DateTimeKind.Utc => value,
            DateTimeKind.Local => value.ToUniversalTime(),
            _ => DateTime.SpecifyKind(value, DateTimeKind.Utc)
        };
    }
}
