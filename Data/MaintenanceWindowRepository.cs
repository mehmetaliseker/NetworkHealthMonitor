using System.Globalization;
using Microsoft.Data.Sqlite;
using NetworkHealthMonitor.Models;

namespace NetworkHealthMonitor.Data;

public sealed class MaintenanceWindowRepository
{
    private readonly SqliteConnectionFactory _connectionFactory;

    public MaintenanceWindowRepository(SqliteConnectionFactory connectionFactory)
    {
        _connectionFactory = connectionFactory;
    }

    public async Task<long> AddAsync(
        MaintenanceWindow window,
        IEnumerable<MaintenanceWindowTarget> targets,
        CancellationToken cancellationToken = default)
    {
        await using var connection = await _connectionFactory.CreateOpenConnectionAsync();
        using var transaction = connection.BeginTransaction();

        await using var insert = connection.CreateCommand();
        insert.Transaction = transaction;
        insert.CommandText = """
            INSERT INTO MaintenanceWindows
                (Name, StartedAtUtc, EndedAtUtc, Reason, SuppressNotifications, ContinuePings, Status, CreatedBy, CreatedAtUtc, UpdatedAtUtc)
            VALUES
                (@Name, @StartedAtUtc, @EndedAtUtc, @Reason, @SuppressNotifications, @ContinuePings, @Status, @CreatedBy, @CreatedAtUtc, @UpdatedAtUtc);
            SELECT last_insert_rowid();
            """;
        AddWindowParameters(insert, window);
        var id = Convert.ToInt64(await insert.ExecuteScalarAsync(cancellationToken), CultureInfo.InvariantCulture);

        await ReplaceTargetsAsync(connection, transaction, id, targets, cancellationToken);

        transaction.Commit();
        return id;
    }

    public async Task UpdateAsync(
        MaintenanceWindow window,
        IEnumerable<MaintenanceWindowTarget> targets,
        CancellationToken cancellationToken = default)
    {
        await using var connection = await _connectionFactory.CreateOpenConnectionAsync();
        using var transaction = connection.BeginTransaction();

        await using var update = connection.CreateCommand();
        update.Transaction = transaction;
        update.CommandText = """
            UPDATE MaintenanceWindows
            SET Name = @Name,
                StartedAtUtc = @StartedAtUtc,
                EndedAtUtc = @EndedAtUtc,
                Reason = @Reason,
                SuppressNotifications = @SuppressNotifications,
                ContinuePings = @ContinuePings,
                Status = @Status,
                CreatedBy = @CreatedBy,
                UpdatedAtUtc = @UpdatedAtUtc
            WHERE Id = @Id;
            """;
        AddWindowParameters(update, window);
        AddParameter(update, "@Id", window.Id);
        await update.ExecuteNonQueryAsync(cancellationToken);

        await ReplaceTargetsAsync(connection, transaction, window.Id, targets, cancellationToken);
        transaction.Commit();
    }

    public async Task<IReadOnlyList<MaintenanceWindowListItem>> GetAllAsync(CancellationToken cancellationToken = default)
    {
        var windows = new Dictionary<long, MaintenanceWindowListItemBuilder>();
        await using var connection = await _connectionFactory.CreateOpenConnectionAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT w.Id, w.Name, w.StartedAtUtc, w.EndedAtUtc, w.Reason, w.SuppressNotifications,
                   w.ContinuePings, w.Status, w.CreatedBy, w.CreatedAtUtc, w.UpdatedAtUtc,
                   t.Id, t.TargetType, t.TargetId
            FROM MaintenanceWindows w
            LEFT JOIN MaintenanceWindowTargets t ON t.MaintenanceWindowId = w.Id
            ORDER BY w.StartedAtUtc DESC, w.Id DESC;
            """;
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            var id = reader.GetInt64(0);
            if (!windows.TryGetValue(id, out var builder))
            {
                builder = new MaintenanceWindowListItemBuilder(ReadWindow(reader));
                windows[id] = builder;
            }

            if (!reader.IsDBNull(11))
            {
                builder.Targets.Add(new MaintenanceWindowTarget
                {
                    Id = reader.GetInt64(11),
                    MaintenanceWindowId = id,
                    TargetType = ParseTargetType(reader.GetString(12)),
                    TargetId = reader.IsDBNull(13) ? null : reader.GetInt32(13)
                });
            }
        }

        return windows.Values.Select(builder => builder.Build()).ToList();
    }

    public Task CancelAsync(long id, CancellationToken cancellationToken = default)
    {
        return SetStatusAsync(id, MaintenanceWindowStatus.Cancelled, DateTime.UtcNow, cancellationToken);
    }

    public Task CompleteAsync(long id, CancellationToken cancellationToken = default)
    {
        return SetStatusAsync(id, MaintenanceWindowStatus.Completed, DateTime.UtcNow, cancellationToken);
    }

    private async Task SetStatusAsync(long id, MaintenanceWindowStatus status, DateTime nowUtc, CancellationToken cancellationToken)
    {
        await using var connection = await _connectionFactory.CreateOpenConnectionAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = """
            UPDATE MaintenanceWindows
            SET Status = @Status,
                EndedAtUtc = CASE WHEN @Status IN ('Completed','Cancelled') AND EndedAtUtc > @NowUtc THEN @NowUtc ELSE EndedAtUtc END,
                UpdatedAtUtc = @NowUtc
            WHERE Id = @Id;
            """;
        AddParameter(command, "@Status", status.ToString());
        AddParameter(command, "@NowUtc", ToStorageDate(nowUtc));
        AddParameter(command, "@Id", id);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static async Task ReplaceTargetsAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        long windowId,
        IEnumerable<MaintenanceWindowTarget> targets,
        CancellationToken cancellationToken)
    {
        var materializedTargets = targets.ToList();
        if (materializedTargets.Count == 0)
        {
            materializedTargets.Add(new MaintenanceWindowTarget { TargetType = MonitoringTargetType.AllDevices });
        }

        await using var delete = connection.CreateCommand();
        delete.Transaction = transaction;
        delete.CommandText = "DELETE FROM MaintenanceWindowTargets WHERE MaintenanceWindowId = @MaintenanceWindowId;";
        AddParameter(delete, "@MaintenanceWindowId", windowId);
        await delete.ExecuteNonQueryAsync(cancellationToken);

        foreach (var target in materializedTargets)
        {
            await using var targetInsert = connection.CreateCommand();
            targetInsert.Transaction = transaction;
            targetInsert.CommandText = """
                INSERT INTO MaintenanceWindowTargets
                    (MaintenanceWindowId, TargetType, TargetId)
                VALUES
                    (@MaintenanceWindowId, @TargetType, @TargetId);
                """;
            AddParameter(targetInsert, "@MaintenanceWindowId", windowId);
            AddParameter(targetInsert, "@TargetType", target.TargetType.ToString());
            AddParameter(targetInsert, "@TargetId", target.TargetId);
            await targetInsert.ExecuteNonQueryAsync(cancellationToken);
        }
    }

    private static void AddWindowParameters(SqliteCommand command, MaintenanceWindow window)
    {
        AddParameter(command, "@Name", window.Name.Trim());
        AddParameter(command, "@StartedAtUtc", ToStorageDate(window.StartedAtUtc));
        AddParameter(command, "@EndedAtUtc", ToStorageDate(window.EndedAtUtc));
        AddParameter(command, "@Reason", window.Reason);
        AddParameter(command, "@SuppressNotifications", window.SuppressNotifications ? 1 : 0);
        AddParameter(command, "@ContinuePings", window.ContinuePings ? 1 : 0);
        AddParameter(command, "@Status", window.Status.ToString());
        AddParameter(command, "@CreatedBy", string.IsNullOrWhiteSpace(window.CreatedBy) ? Environment.UserName : window.CreatedBy);
        AddParameter(command, "@CreatedAtUtc", ToStorageDate(window.CreatedAtUtc == default ? DateTime.UtcNow : window.CreatedAtUtc));
        AddParameter(command, "@UpdatedAtUtc", ToStorageDate(window.UpdatedAtUtc == default ? DateTime.UtcNow : window.UpdatedAtUtc));
    }

    private static MaintenanceWindow ReadWindow(SqliteDataReader reader)
    {
        return new MaintenanceWindow
        {
            Id = reader.GetInt64(0),
            Name = reader.GetString(1),
            StartedAtUtc = FromStorageDate(reader.GetString(2)),
            EndedAtUtc = FromStorageDate(reader.GetString(3)),
            Reason = reader.GetString(4),
            SuppressNotifications = reader.GetInt32(5) == 1,
            ContinuePings = reader.GetInt32(6) == 1,
            Status = Enum.TryParse<MaintenanceWindowStatus>(reader.GetString(7), true, out var status) ? status : MaintenanceWindowStatus.Scheduled,
            CreatedBy = reader.GetString(8),
            CreatedAtUtc = FromStorageDate(reader.GetString(9)),
            UpdatedAtUtc = FromStorageDate(reader.GetString(10))
        };
    }

    private static MonitoringTargetType ParseTargetType(string value)
    {
        return Enum.TryParse<MonitoringTargetType>(value, true, out var parsed)
            ? parsed
            : MonitoringTargetType.AllDevices;
    }

    private sealed class MaintenanceWindowListItemBuilder
    {
        public MaintenanceWindowListItemBuilder(MaintenanceWindow window)
        {
            Window = window;
        }

        public MaintenanceWindow Window { get; }

        public List<MaintenanceWindowTarget> Targets { get; } = new();

        public MaintenanceWindowListItem Build()
        {
            return new MaintenanceWindowListItem
            {
                Window = Window,
                Targets = Targets
            };
        }
    }

    private static void AddParameter(SqliteCommand command, string name, object? value)
    {
        command.Parameters.AddWithValue(name, value ?? DBNull.Value);
    }

    private static string ToStorageDate(DateTime value)
    {
        return value.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture);
    }

    private static DateTime FromStorageDate(string value)
    {
        return DateTime.TryParse(
            value,
            CultureInfo.InvariantCulture,
            DateTimeStyles.RoundtripKind,
            out var parsed)
            ? parsed.ToUniversalTime()
            : DateTime.UtcNow;
    }
}
