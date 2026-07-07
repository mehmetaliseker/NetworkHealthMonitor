namespace NetworkHealthMonitor.Models;

public sealed class Outage
{
    public int Id { get; set; }

    public int DeviceId { get; set; }

    public string DeviceName { get; set; } = string.Empty;

    public string IpAddress { get; set; } = string.Empty;

    public DeviceType DeviceType { get; set; }

    public string GroupName { get; set; } = string.Empty;

    public DateTime StartedAt { get; set; }

    public DateTime? EndedAt { get; set; }

    public int FailureCount { get; set; }

    public int? RecoveryPingLogId { get; set; }

    public bool IsResolved { get; set; }

    public string DeviceTypeText => DeviceType.ToDisplayName();

    public string StartedAtText => StartedAt.ToString("dd.MM.yyyy HH:mm:ss");

    public string EndedAtText => EndedAt.HasValue ? EndedAt.Value.ToString("dd.MM.yyyy HH:mm:ss") : "-";

    public string DurationText
    {
        get
        {
            var end = EndedAt ?? DateTime.Now;
            var duration = end - StartedAt;
            if (duration.TotalMinutes < 1)
            {
                return $"{Math.Max(0, (int)duration.TotalSeconds)} sn";
            }

            if (duration.TotalHours < 1)
            {
                return $"{duration.TotalMinutes:0} dk";
            }

            return $"{duration.TotalHours:0.0} sa";
        }
    }

    public string StatusText => IsResolved ? "Kapandı" : "Açık";
}
