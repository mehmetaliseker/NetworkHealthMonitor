namespace NetworkHealthMonitor.Services;

public sealed class SystemClock : ISystemClock, IClock
{
    public DateTime Now => DateTime.Now;

    public DateTime UtcNow => DateTime.UtcNow;
}
