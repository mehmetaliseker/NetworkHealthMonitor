namespace NetworkHealthMonitor.Services;

public interface IClock
{
    DateTime UtcNow { get; }
}
