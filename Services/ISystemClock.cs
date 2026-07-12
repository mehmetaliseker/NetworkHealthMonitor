namespace NetworkHealthMonitor.Services;

public interface ISystemClock
{
    DateTime Now { get; }
}
