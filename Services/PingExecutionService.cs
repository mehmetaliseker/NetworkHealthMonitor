using System.Collections.Concurrent;
using System.IO;
using NetworkHealthMonitor.Data;
using NetworkHealthMonitor.Models;

namespace NetworkHealthMonitor.Services;

public sealed class PingExecutionService : IPingExecutionService
{
    private readonly DeviceRepository _deviceRepository;
    private readonly DeviceGroupRepository _deviceGroupRepository;
    private readonly PingLogRepository _pingLogRepository;
    private readonly OutageRepository _outageRepository;
    private readonly IPingService _pingService;
    private readonly IDeviceCheckPolicyService _deviceCheckPolicyService;
    private readonly IDeviceHealthEvaluator _deviceHealthEvaluator;
    private readonly AppSettingsService _settingsService;
    private readonly ConcurrentDictionary<int, byte> _runningDeviceIds = new();

    public PingExecutionService(
        DeviceRepository deviceRepository,
        DeviceGroupRepository deviceGroupRepository,
        PingLogRepository pingLogRepository,
        OutageRepository outageRepository,
        IPingService pingService,
        IDeviceCheckPolicyService deviceCheckPolicyService,
        IDeviceHealthEvaluator deviceHealthEvaluator,
        AppSettingsService settingsService)
    {
        _deviceRepository = deviceRepository;
        _deviceGroupRepository = deviceGroupRepository;
        _pingLogRepository = pingLogRepository;
        _outageRepository = outageRepository;
        _pingService = pingService;
        _deviceCheckPolicyService = deviceCheckPolicyService;
        _deviceHealthEvaluator = deviceHealthEvaluator;
        _settingsService = settingsService;
    }

    public async Task<PingExecutionResult> PingDevicesAsync(
        IEnumerable<Device> devices,
        PingOptions options,
        PingTriggerType triggerType,
        SchedulePlan? schedulePlan = null,
        IProgress<PingProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        var candidates = devices
            .Where(device => device.Id > 0 && device.IsActive)
            .DistinctBy(device => device.Id)
            .ToList();

        var acquired = new List<DevicePingLease>();
        var skipped = 0;
        foreach (var device in candidates)
        {
            if (!_runningDeviceIds.TryAdd(device.Id, 0))
            {
                skipped++;
                continue;
            }

            var lease = DevicePingLease.TryAcquire(device);
            if (lease is not null)
            {
                acquired.Add(lease);
                continue;
            }

            _runningDeviceIds.TryRemove(device.Id, out _);
            skipped++;
        }

        if (acquired.Count == 0)
        {
            return new PingExecutionResult(Array.Empty<PingDeviceResult>(), Array.Empty<PingLog>(), skipped);
        }

        try
        {
            var settings = await _settingsService.LoadAsync();
            var groups = await _deviceGroupRepository.GetAllAsync();
            var groupsById = groups.ToDictionary(group => group.Id);
            var groupsByName = groups.ToDictionary(group => group.Name, StringComparer.OrdinalIgnoreCase);
            var acquiredDevices = acquired.Select(item => item.Device).ToList();
            var policies = acquiredDevices.ToDictionary(
                device => device.Id,
                device => _deviceCheckPolicyService.ResolvePolicy(
                    device,
                    ResolveGroup(device, groupsById, groupsByName),
                    schedulePlan,
                    settings,
                    options));
            var rawResults = await PingByEffectiveTimeoutAsync(acquiredDevices, policies, options, progress, cancellationToken);
            var results = rawResults
                .Select(result =>
                {
                    var policy = policies[result.Device.Id];
                    return result with { Status = _deviceHealthEvaluator.Evaluate(result.Device, result, policy) };
                })
                .ToList();
            var logs = results
                .Select(result => CreateLog(result, triggerType, schedulePlan))
                .ToList();

            await _pingLogRepository.AddRangeAsync(logs);
            await _deviceRepository.BulkUpdatePingResultsAsync(results);
            await _outageRepository.SyncFromPingResultsAsync(results, CreateRecoveryLogMap(logs));

            return new PingExecutionResult(results, logs, skipped);
        }
        finally
        {
            foreach (var device in acquired)
            {
                _runningDeviceIds.TryRemove(device.Device.Id, out _);
                device.Dispose();
            }
        }
    }

    private async Task<IReadOnlyList<PingDeviceResult>> PingByEffectiveTimeoutAsync(
        IReadOnlyList<Device> devices,
        IReadOnlyDictionary<int, DeviceCheckPolicy> policies,
        PingOptions baseOptions,
        IProgress<PingProgress>? progress,
        CancellationToken cancellationToken)
    {
        var allResults = new List<PingDeviceResult>();
        var total = devices.Count;
        var completedOffset = 0;
        var successOffset = 0;
        var failureOffset = 0;

        foreach (var group in devices.GroupBy(device => policies[device.Id].PingTimeoutMs).OrderBy(group => group.Key))
        {
            var groupDevices = group.ToList();
            var groupProgress = progress is null
                ? null
                : new Progress<PingProgress>(item =>
                {
                    progress.Report(new PingProgress(
                        total,
                        completedOffset + item.Completed,
                        successOffset + item.Success,
                        failureOffset + item.Failure,
                        item.DeviceId,
                        item.DeviceStatus,
                        item.LatencyMs,
                        item.CheckedAt));
                });

            var groupOptions = baseOptions with { TimeoutMs = group.Key };
            var groupResults = await _pingService.PingManyAsync(groupDevices, groupOptions, groupProgress, cancellationToken);
            allResults.AddRange(groupResults);
            completedOffset += groupResults.Count;
            successOffset += groupResults.Count(result => result.IsSuccess);
            failureOffset += groupResults.Count(result => !result.IsSuccess);
        }

        return allResults.OrderBy(result => result.CheckedAt).ToList();
    }

    private static IReadOnlyDictionary<int, int?> CreateRecoveryLogMap(IReadOnlyList<PingLog> logs)
    {
        return logs
            .Where(log => log.DeviceId.HasValue)
            .ToDictionary(log => log.DeviceId!.Value, log => (int?)log.Id);
    }

    private static PingLog CreateLog(
        PingDeviceResult result,
        PingTriggerType triggerType,
        SchedulePlan? schedulePlan)
    {
        return new PingLog
        {
            DeviceId = result.Device.Id,
            DeviceName = result.Device.Name,
            IpAddress = result.Device.IpAddress,
            DeviceType = result.Device.DeviceType,
            GroupName = result.Device.GroupName,
            Status = result.Status,
            LatencyMs = result.LatencyMs,
            ResponseMessage = result.ResponseMessage,
            ErrorMessage = result.ErrorMessage,
            CheckedAt = result.CheckedAt,
            TriggerType = triggerType,
            SchedulePlanId = schedulePlan?.Id,
            SchedulePlanName = schedulePlan?.Name ?? string.Empty
        };
    }

    private static DeviceGroup? ResolveGroup(
        Device device,
        IReadOnlyDictionary<int, DeviceGroup> groupsById,
        IReadOnlyDictionary<string, DeviceGroup> groupsByName)
    {
        if (device.GroupId.HasValue && groupsById.TryGetValue(device.GroupId.Value, out var groupById))
        {
            return groupById;
        }

        return !string.IsNullOrWhiteSpace(device.GroupName) && groupsByName.TryGetValue(device.GroupName, out var groupByName)
            ? groupByName
            : null;
    }

    private sealed class DevicePingLease : IDisposable
    {
        private readonly FileStream _lockStream;
        private bool _disposed;

        private DevicePingLease(Device device, FileStream lockStream)
        {
            Device = device;
            _lockStream = lockStream;
        }

        public Device Device { get; }

        public static DevicePingLease? TryAcquire(Device device)
        {
            var lockDirectory = Path.Combine(DatabasePaths.DataDirectory, "locks");
            Directory.CreateDirectory(lockDirectory);
            var lockPath = Path.Combine(lockDirectory, $"device-{device.Id}.lock");
            try
            {
                var stream = new FileStream(lockPath, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
                return new DevicePingLease(device, stream);
            }
            catch (IOException)
            {
                return null;
            }
            catch
            {
                throw;
            }
        }

        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            _lockStream.Dispose();
            _disposed = true;
        }
    }
}
