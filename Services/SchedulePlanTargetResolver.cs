using NetworkHealthMonitor.Models;

namespace NetworkHealthMonitor.Services;

public sealed class SchedulePlanTargetResolver
{
    public IReadOnlyList<Device> ResolveTargets(
        SchedulePlan plan,
        IEnumerable<Device> devices,
        IEnumerable<DeviceGroup> groups,
        bool respectAutoCheck)
    {
        var eligibleDevices = devices
            .Where(device => device.IsActive && (!respectAutoCheck || device.AutoCheckEnabled))
            .ToList();

        var groupIdsAssignedToPlan = groups
            .Where(group => group.DefaultSchedulePlanId == plan.Id)
            .Select(group => group.Id)
            .ToHashSet();
        var groupNamesAssignedToPlan = groups
            .Where(group => group.DefaultSchedulePlanId == plan.Id)
            .Select(group => group.Name)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var targets = new Dictionary<int, Device>();
        AddTargets(targets, ResolveExplicitTargets(plan, eligibleDevices));
        AddTargets(targets, eligibleDevices.Where(device => device.DefaultSchedulePlanId == plan.Id));
        AddTargets(targets, eligibleDevices.Where(device =>
            (device.GroupId.HasValue && groupIdsAssignedToPlan.Contains(device.GroupId.Value))
            || groupNamesAssignedToPlan.Contains(device.GroupName)));

        return targets.Values.ToList();
    }

    private static IEnumerable<Device> ResolveExplicitTargets(SchedulePlan plan, IReadOnlyList<Device> devices)
    {
        return plan.TargetType switch
        {
            SchedulePlanTargetType.Device => devices.Where(device => MatchesDeviceTarget(device, plan.TargetValue)),
            SchedulePlanTargetType.DeviceType => devices.Where(device => device.DeviceType == DeviceTypeExtensions.FromStorageValue(plan.TargetValue)),
            SchedulePlanTargetType.DeviceGroup => devices.Where(device => MatchesGroupTarget(device, plan.TargetValue)),
            SchedulePlanTargetType.CriticalDevices => devices.Where(device => device.IsCritical),
            SchedulePlanTargetType.AllDevices => devices,
            _ => devices
        };
    }

    private static bool MatchesDeviceTarget(Device device, string targetValue)
    {
        return int.TryParse(targetValue, out var id)
            ? device.Id == id
            : string.Equals(device.IpAddress, targetValue, StringComparison.OrdinalIgnoreCase);
    }

    private static bool MatchesGroupTarget(Device device, string targetValue)
    {
        return int.TryParse(targetValue, out var id)
            ? device.GroupId == id
            : string.Equals(device.GroupName, targetValue, StringComparison.OrdinalIgnoreCase);
    }

    private static void AddTargets(IDictionary<int, Device> targets, IEnumerable<Device> devices)
    {
        foreach (var device in devices)
        {
            targets.TryAdd(device.Id, device);
        }
    }
}
