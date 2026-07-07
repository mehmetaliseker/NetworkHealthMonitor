using System.Collections.Concurrent;
using NetworkHealthMonitor.Data;
using NetworkHealthMonitor.Models;

namespace NetworkHealthMonitor.Services;

public sealed class PingExecutionService : IPingExecutionService
{
    private readonly DeviceRepository _deviceRepository;
    private readonly PingLogRepository _pingLogRepository;
    private readonly OutageRepository _outageRepository;
    private readonly IPingService _pingService;
    private readonly ConcurrentDictionary<int, byte> _runningDeviceIds = new();

    public PingExecutionService(
        DeviceRepository deviceRepository,
        PingLogRepository pingLogRepository,
        OutageRepository outageRepository,
        IPingService pingService)
    {
        _deviceRepository = deviceRepository;
        _pingLogRepository = pingLogRepository;
        _outageRepository = outageRepository;
        _pingService = pingService;
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

        var acquired = new List<Device>();
        var skipped = 0;
        foreach (var device in candidates)
        {
            if (_runningDeviceIds.TryAdd(device.Id, 0))
            {
                acquired.Add(device);
                continue;
            }

            skipped++;
        }

        if (acquired.Count == 0)
        {
            return new PingExecutionResult(Array.Empty<PingDeviceResult>(), Array.Empty<PingLog>(), skipped);
        }

        try
        {
            var results = await _pingService.PingManyAsync(acquired, options, progress, cancellationToken);
            var logs = results
                .Select(result => CreateLog(result, triggerType, schedulePlan))
                .ToList();

            await _pingLogRepository.AddRangeAsync(logs);
            await _deviceRepository.BulkUpdatePingResultsAsync(results, options.FailureThreshold);
            await UpdateOutagesAsync(results, logs, options.FailureThreshold);

            return new PingExecutionResult(results, logs, skipped);
        }
        finally
        {
            foreach (var device in acquired)
            {
                _runningDeviceIds.TryRemove(device.Id, out _);
            }
        }
    }

    private async Task UpdateOutagesAsync(
        IReadOnlyList<PingDeviceResult> results,
        IReadOnlyList<PingLog> logs,
        int failureThreshold)
    {
        var logByDevice = logs
            .Where(log => log.DeviceId.HasValue)
            .ToDictionary(log => log.DeviceId!.Value);

        foreach (var result in results)
        {
            var device = result.Device;
            if (result.IsSuccess)
            {
                int? recoveryLogId = logByDevice.TryGetValue(device.Id, out var successLog) ? successLog.Id : null;
                await _outageRepository.ResolveByDeviceIdAsync(device.Id, result.CheckedAt, recoveryLogId);
                continue;
            }

            var failureCount = device.ConsecutiveFailures + 1;
            if (failureCount < failureThreshold)
            {
                continue;
            }

            var openOutage = await _outageRepository.GetOpenByDeviceIdAsync(device.Id);
            if (openOutage is null)
            {
                await _outageRepository.StartAsync(device, result.CheckedAt, failureCount);
            }
            else
            {
                await _outageRepository.UpdateFailureCountAsync(openOutage.Id, failureCount);
            }
        }
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
}
