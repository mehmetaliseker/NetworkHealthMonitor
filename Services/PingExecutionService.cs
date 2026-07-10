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
    private readonly IDeviceCheckPolicyService _deviceCheckPolicyService;
    private readonly IDeviceHealthEvaluator _deviceHealthEvaluator;
    private readonly AppSettingsService _settingsService;
    private readonly ConcurrentDictionary<int, byte> _runningDeviceIds = new();

    public PingExecutionService(
        DeviceRepository deviceRepository,
        PingLogRepository pingLogRepository,
        OutageRepository outageRepository,
        IPingService pingService,
        IDeviceCheckPolicyService deviceCheckPolicyService,
        IDeviceHealthEvaluator deviceHealthEvaluator,
        AppSettingsService settingsService)
    {
        _deviceRepository = deviceRepository;
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
            var rawResults = await _pingService.PingManyAsync(acquired, options, progress, cancellationToken);
            var settings = await _settingsService.LoadAsync();
            var results = rawResults
                .Select(result =>
                {
                    var policy = _deviceCheckPolicyService.ResolvePolicy(result.Device, null, schedulePlan, settings, options);
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
                _runningDeviceIds.TryRemove(device.Id, out _);
            }
        }
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
}
