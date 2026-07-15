namespace NetworkHealthMonitor.Models;

public sealed class PingLog
{
    public int Id { get; set; }

    public int? DeviceId { get; set; }

    public string DeviceName { get; set; } = string.Empty;

    public string IpAddress { get; set; } = string.Empty;

    public DeviceType DeviceType { get; set; }

    public string GroupName { get; set; } = string.Empty;

    public DeviceStatus Status { get; set; }

    public bool IsReachable { get; set; }

    public long? LatencyMs { get; set; }

    public string ResponseMessage { get; set; } = string.Empty;

    public string ErrorCode { get; set; } = string.Empty;

    public string ErrorMessage { get; set; } = string.Empty;

    public DateTime CheckedAt { get; set; }

    public string Source { get; set; } = "Manual";

    public PingTriggerType TriggerType { get; set; } = PingTriggerType.Manual;

    public int? PlanId { get; set; }

    public int? SchedulePlanId { get; set; }

    public string SchedulePlanName { get; set; } = string.Empty;

    public string WorkerInstanceId { get; set; } = string.Empty;

    public string DeviceTypeText => DeviceType.ToDisplayName();

    public string StatusText => Status.ToDisplayName();

    public string LatencyText => LatencyMs.HasValue ? $"{LatencyMs.Value} ms" : "-";

    public string CheckedAtText => CheckedAt.ToString("dd.MM.yyyy HH:mm:ss");

    public string TriggerTypeText => TriggerType.ToDisplayName();

    public string ResponseOrErrorText => string.IsNullOrWhiteSpace(ErrorMessage) ? ResponseMessage : ErrorMessage;
}
