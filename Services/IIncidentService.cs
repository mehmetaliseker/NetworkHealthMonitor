using NetworkHealthMonitor.Models;

namespace NetworkHealthMonitor.Services;

public interface IIncidentService
{
    Task ApplyPingResultsAsync(
        IReadOnlyList<PingDeviceResult> results,
        IReadOnlyDictionary<int, PingLog> logsByDeviceId,
        CancellationToken cancellationToken = default);

    Task EvaluateOpenIncidentsAsync(CancellationToken cancellationToken = default);
}
