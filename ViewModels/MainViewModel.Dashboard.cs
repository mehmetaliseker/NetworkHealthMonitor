using System.Globalization;
using NetworkHealthMonitor.Models;

namespace NetworkHealthMonitor.ViewModels;

public sealed partial class MainViewModel
{
    private void UpdateDashboard()
    {
        if (SummaryCards.Count < 12)
        {
            EnsureSummaryCards();
        }

        var activeDevices = Devices.Count(device => !device.IsDeleted && device.IsActive && device.IsEnabled);
        var statusGroups = AvailabilityItems.GroupBy(item => item.CurrentAvailabilityStatus).ToDictionary(group => group.Key, group => group.Count());

        SummaryCards[0].Value = activeDevices.ToString(CultureInfo.CurrentCulture);
        SummaryCards[1].Value = statusGroups.GetValueOrDefault(AvailabilityStatus.Up).ToString(CultureInfo.CurrentCulture);
        SummaryCards[2].Value = statusGroups.GetValueOrDefault(AvailabilityStatus.Down).ToString(CultureInfo.CurrentCulture);
        SummaryCards[3].Value = statusGroups.GetValueOrDefault(AvailabilityStatus.Unknown).ToString(CultureInfo.CurrentCulture);
        SummaryCards[4].Value = statusGroups.GetValueOrDefault(AvailabilityStatus.Maintenance).ToString(CultureInfo.CurrentCulture);
        SummaryCards[5].Value = OpenOutages.Count.ToString(CultureInfo.CurrentCulture);
        SummaryCards[6].Value = FormatPercent(_dashboardAvailability24Hours);
        SummaryCards[7].Value = FormatPercent(_dashboardAvailability7Days);
        SummaryCards[8].Value = FormatPercent(_dashboardAvailability30Days);
        SummaryCards[9].Value = FormatPercent(_dashboardCoverage30Days);
        SummaryCards[10].Value = _dashboardSlaViolationCount.ToString(CultureInfo.CurrentCulture);
        SummaryCards[11].Value = FailedNotificationCount.ToString(CultureInfo.CurrentCulture);

        ReplaceCollection(CriticalProblemDevices, Devices.Where(device => device.IsCritical && device.LastStatus.IsProblematic()).Take(10));
        ReplaceCollection(RecentFailureLogs, Logs.Where(log => log.Status.IsFailureObservation()).Take(10));
        ReplaceCollection(RecentDashboardLogs, Logs.Take(12));
        ReplaceCollection(LowAvailabilityDevices, AvailabilityItems
            .Where(item => item.Availability30DaysPercent.HasValue)
            .OrderBy(item => item.Availability30DaysPercent)
            .Take(10));
        UpdateTypeDistributionRows();
        NotifyShellMetrics();
    }

    private void EnsureSummaryCards()
    {
        var cards = new[]
        {
            new SummaryCardViewModel("Toplam aktif cihaz", "0", "#2563EB"),
            new SummaryCardViewModel("Erişilebilir", "0", "#16A34A"),
            new SummaryCardViewModel("Erişilemiyor", "0", "#DC2626"),
            new SummaryCardViewModel("Kontrol edilmedi", "0", "#64748B"),
            new SummaryCardViewModel("Bakımda", "0", "#0F766E"),
            new SummaryCardViewModel("Açık kesinti", "0", "#EA580C"),
            new SummaryCardViewModel("24 saat erişilebilirlik", "-", "#0F766E"),
            new SummaryCardViewModel("7 gün erişilebilirlik", "-", "#0F766E"),
            new SummaryCardViewModel("30 gün erişilebilirlik", "-", "#0F766E"),
            new SummaryCardViewModel("Genel kapsama", "-", "#475569"),
            new SummaryCardViewModel("SLA ihlali", "0", "#DC2626"),
            new SummaryCardViewModel("Başarısız bildirim", "0", "#DC2626")
        };

        SummaryCards.Clear();
        foreach (var card in cards)
        {
            SummaryCards.Add(card);
        }
    }

    private static string FormatPercent(double? value)
    {
        return value.HasValue ? $"{value.Value:0.0}%" : "-";
    }

    private void UpdateTypeDistributionRows()
    {
        var total = Math.Max(1, Devices.Count);
        var rows = Devices
            .GroupBy(device => device.DeviceType)
            .OrderBy(group => group.Key.ToDisplayName())
            .Select(group => new MetricRowViewModel
            {
                Title = group.Key.ToDisplayName(),
                Value = group.Count().ToString(CultureInfo.CurrentCulture),
                Percent = group.Count() * 100d / total,
                AccentColor = group.Any(device => device.LastStatus.IsProblematic()) ? "#DC2626" : "#2563EB"
            });

        ReplaceCollection(DeviceTypeDistribution, rows);
    }

    private void UpdateGroupAvailabilityRows()
    {
        var rows = AvailabilityItems
            .Where(item => !string.IsNullOrWhiteSpace(item.GroupName) && item.Availability30DaysPercent.HasValue)
            .GroupBy(item => item.GroupName)
            .OrderBy(group => group.Key)
            .Select(group =>
            {
                var knownSeconds = group.Sum(item => item.UpSeconds + item.DownSeconds);
                var average = knownSeconds > 0
                    ? group.Sum(item => item.UpSeconds) * 100d / knownSeconds
                    : group.Average(item => item.Availability30DaysPercent!.Value);
                return new MetricRowViewModel
                {
                    Title = group.Key,
                    Value = $"{average:0.0}%",
                    Percent = average,
                    AccentColor = average >= 99 ? "#16A34A" : average >= 95 ? "#EA580C" : "#DC2626"
                };
            });

        ReplaceCollection(GroupAvailabilityRows, rows);
    }

    private void ApplyGroupAvailabilityToGroups()
    {
        var availabilityByGroup = AvailabilityItems
            .Where(item => !string.IsNullOrWhiteSpace(item.GroupName) && item.Availability30DaysPercent.HasValue)
            .GroupBy(item => item.GroupName)
            .ToDictionary(
                group => group.Key,
                group =>
                {
                    var knownSeconds = group.Sum(item => item.UpSeconds + item.DownSeconds);
                    return knownSeconds > 0
                        ? (double?)group.Sum(item => item.UpSeconds) * 100d / knownSeconds
                        : group.Average(item => item.Availability30DaysPercent!.Value);
                },
                StringComparer.OrdinalIgnoreCase);

        foreach (var group in DeviceGroups)
        {
            group.Availability30DaysPercent = availabilityByGroup.TryGetValue(group.Name, out var availability)
                ? availability
                : null;
        }
    }
}

