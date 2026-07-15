using NetworkHealthMonitor.Data;

namespace NetworkHealthMonitor.Worker;

public static class WorkerMaintenance
{
    public static async Task<int> RunHealthCheckAsync(WorkerOptions options)
    {
        DatabasePaths.Configure(options.PathProvider, options.LegacyDataDirectory);
        var errors = new List<string>();

        if (!File.Exists(DatabasePaths.DatabaseFilePath))
        {
            Console.Error.WriteLine($"FAIL SQLite DB yok: {DatabasePaths.DatabaseFilePath}");
            return 1;
        }

        try
        {
            var connectionFactory = new SqliteConnectionFactory();
            await connectionFactory.InitializeAsync();
            var heartbeatRepository = new WorkerHeartbeatRepository(connectionFactory);
            var outboxRepository = new NotificationOutboxRepository(connectionFactory);
            var heartbeat = await heartbeatRepository.GetLatestAsync();
            if (options.SkipHeartbeatCheck)
            {
                Console.WriteLine("Heartbeat=Skipped");
            }
            else if (heartbeat is null)
            {
                errors.Add("Heartbeat kaydi yok.");
            }
            else
            {
                var age = DateTime.UtcNow - heartbeat.LastSeenAtUtc;
                Console.WriteLine($"HeartbeatAgeSeconds={Math.Max(0, (int)age.TotalSeconds)}");
                Console.WriteLine($"WorkerInstanceId={heartbeat.WorkerInstanceId}");
                Console.WriteLine($"WorkerStatus={heartbeat.Status}");
                if (age.TotalSeconds > options.HeartbeatMaxAgeSeconds)
                {
                    errors.Add($"Heartbeat eski: {(int)age.TotalSeconds} sn.");
                }

                if (!string.IsNullOrWhiteSpace(heartbeat.LastError))
                {
                    errors.Add($"Worker son hata: {heartbeat.LastError}");
                }
            }

            var counts = await outboxRepository.GetCountsAsync();
            Console.WriteLine($"OutboxPending={counts.Pending}");
            Console.WriteLine($"OutboxFailed={counts.Failed}");
            Console.WriteLine("SQLite=OK");
        }
        catch (Exception ex)
        {
            errors.Add($"SQLite DB acilamadi: {ex.Message}");
        }

        foreach (var error in errors)
        {
            Console.Error.WriteLine("FAIL " + error);
        }

        return errors.Count == 0 ? 0 : 1;
    }
}
