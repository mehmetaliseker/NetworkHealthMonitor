namespace NetworkHealthMonitor.Services;

public sealed class SystemClock : ISystemClock
{
    public DateTime Now => DateTime.Now;
}
