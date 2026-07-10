using System.Globalization;
using NetworkHealthMonitor.Models;

namespace NetworkHealthMonitor.ViewModels;

public sealed partial class MainViewModel
{
    private void UpdateDashboard()
    {
        if (SummaryCards.Count < 8)
        {
            EnsureSummaryCards();
        }

        var total = Devices.Count;
        var healthy = Devices.Count(device => device.LastStatus.IsSuccessful());
        var underWatch = Devices.Count(device => device.LastStatus is DeviceStatus.Warning or DeviceStatus.UnderWatch or DeviceStatus.PingBlockedOrNoReply);
        var probableOffline = Devices.Count(device => device.LastStatus == DeviceStatus.Offline);
        var notChecked = Devices.Count(device => device.LastStatus == DeviceStatus.Unknown);
        double? averageUptime = Devices.Any(device => device.Uptime30DaysPercent.HasValue)
            ? Devices.Where(device => device.Uptime30DaysPercent.HasValue).Average(device => device.Uptime30DaysPercent!.Value)
            : null;
        var activePlans = SchedulePlans.Count(plan => plan.IsActive);

        SummaryCards[0].Value = total.ToString(CultureInfo.CurrentCulture);
        SummaryCards[1].Value = healthy.ToString(CultureInfo.CurrentCulture);
        SummaryCards[2].Value = underWatch.ToString(CultureInfo.CurrentCulture);
        SummaryCards[3].Value = probableOffline.ToString(CultureInfo.CurrentCulture);
        SummaryCards[4].Value = notChecked.ToString(CultureInfo.CurrentCulture);
        SummaryCards[5].Value = averageUptime.HasValue ? $"{averageUptime.Value:0.0}%" : "-";
        SummaryCards[6].Value = activePlans.ToString(CultureInfo.CurrentCulture);
        SummaryCards[7].Value = OpenOutages.Count.ToString(CultureInfo.CurrentCulture);

        ReplaceCollection(CriticalProblemDevices, Devices.Where(device => device.IsCritical && device.LastStatus.IsProblematic()).Take(10));
        ReplaceCollection(RecentFailureLogs, Logs.Where(log => log.Status.IsFailureObservation()).Take(10));
        ReplaceCollection(RecentDashboardLogs, Logs.Take(12));
        ReplaceCollection(LowAvailabilityDevices, AvailabilityItems
            .Where(item => item.Availability30DaysPercent.HasValue)
            .OrderBy(item => item.Availability30DaysPercent)
            .Take(10));
        UpdateTypeDistributionRows();
    }

    private void EnsureSummaryCards()
    {
        if (SummaryCards.Count > 0)
        {
            return;
        }

        SummaryCards.Add(new SummaryCardViewModel("Toplam cihaz", "0", "#2563EB"));
        SummaryCards.Add(new SummaryCardViewModel("Sağlıklı", "0", "#16A34A"));
        SummaryCards.Add(new SummaryCardViewModel("Takipte", "0", "#EA580C"));
        SummaryCards.Add(new SummaryCardViewModel("Muhtemel erişilemiyor", "0", "#DC2626"));
        SummaryCards.Add(new SummaryCardViewModel("Kontrol edilmedi", "0", "#64748B"));
        SummaryCards.Add(new SummaryCardViewModel("Ortalama uptime", "-", "#0F766E"));
        SummaryCards.Add(new SummaryCardViewModel("Aktif plan", "0", "#7C3AED"));
        SummaryCards.Add(new SummaryCardViewModel("Açık kesinti", "0", "#EA580C"));
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
                var average = group.Average(item => item.Availability30DaysPercent!.Value);
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
                group => (double?)group.Average(item => item.Availability30DaysPercent!.Value),
                StringComparer.OrdinalIgnoreCase);

        foreach (var group in DeviceGroups)
        {
            group.Availability30DaysPercent = availabilityByGroup.TryGetValue(group.Name, out var availability)
                ? availability
                : null;
        }
    }
}

