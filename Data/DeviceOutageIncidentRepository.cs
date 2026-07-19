using System.Globalization;
using Microsoft.Data.Sqlite;
using NetworkHealthMonitor.Models;
using NetworkHealthMonitor.Services;

namespace NetworkHealthMonitor.Data;

public sealed class DeviceOutageIncidentRepository : IDeviceOutageIncidentRepository
{
    private readonly SqliteConnectionFactory _connectionFactory;

    public DeviceOutageIncidentRepository(SqliteConnectionFactory connectionFactory)
    {
        _connectionFactory = connectionFactory;
    }

    public async Task MarkNotificationSentAsync(
        long incidentId,
        string eventType,
        DateTime sentAtUtc,
        CancellationToken cancellationToken = default)
    {
        if (incidentId <= 0)
        {
            return;
        }

        var column = eventType switch
        {
            NotificationEventTypes.DeviceSuspectedOffline or NotificationEventTypes.DeviceDownLegacy => "InitialNotificationSentAtUtc",
            NotificationEventTypes.DeviceOfflineEscalated => "EscalationNotificationSentAtUtc",
            NotificationEventTypes.DeviceRecovered => "RecoveryNotificationCreatedAtUtc",
            _ => string.Empty
        };
        if (string.IsNullOrWhiteSpace(column))
        {
            return;
        }

        await using var connection = await _connectionFactory.CreateOpenConnectionAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = $"""
            UPDATE DeviceIncidents
            SET {column} = COALESCE({column}, @SentAtUtc),
                DownNotificationCreatedAtUtc = CASE
                    WHEN @EventType IN ('DeviceSuspectedOffline','DeviceDown') THEN COALESCE(DownNotificationCreatedAtUtc, @SentAtUtc)
                    ELSE DownNotificationCreatedAtUtc
                END,
                UpdatedAtUtc = @SentAtUtc
            WHERE Id = @IncidentId;
            """;
        command.Parameters.AddWithValue("@SentAtUtc", sentAtUtc.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture));
        command.Parameters.AddWithValue("@EventType", eventType);
        command.Parameters.AddWithValue("@IncidentId", incidentId);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }
}
