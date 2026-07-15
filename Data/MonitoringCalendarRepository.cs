using System.Globalization;
using Microsoft.Data.Sqlite;
using NetworkHealthMonitor.Models;

namespace NetworkHealthMonitor.Data;

public sealed class MonitoringCalendarRepository
{
    private readonly SqliteConnectionFactory _connectionFactory;

    public MonitoringCalendarRepository(SqliteConnectionFactory connectionFactory)
    {
        _connectionFactory = connectionFactory;
    }

    public async Task<long> AddAsync(
        MonitoringCalendar calendar,
        IEnumerable<MonitoringCalendarRule> rules,
        IEnumerable<DeviceMonitoringCalendarAssignment> assignments,
        CancellationToken cancellationToken = default)
    {
        await using var connection = await _connectionFactory.CreateOpenConnectionAsync();
        using var transaction = connection.BeginTransaction();
        await ClearDefaultIfNeededAsync(connection, transaction, calendar.IsDefault, cancellationToken);

        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            INSERT INTO MonitoringCalendars
                (Name, TimezoneId, IsDefault, CreatedAtUtc, UpdatedAtUtc)
            VALUES
                (@Name, @TimezoneId, @IsDefault, @CreatedAtUtc, @UpdatedAtUtc);
            SELECT last_insert_rowid();
            """;
        AddCalendarParameters(command, calendar);
        var id = Convert.ToInt64(await command.ExecuteScalarAsync(cancellationToken), CultureInfo.InvariantCulture);
        await ReplaceRulesAsync(connection, transaction, id, rules, cancellationToken);
        await ReplaceAssignmentsAsync(connection, transaction, id, assignments, cancellationToken);
        transaction.Commit();
        return id;
    }

    public async Task UpdateAsync(
        MonitoringCalendar calendar,
        IEnumerable<MonitoringCalendarRule> rules,
        IEnumerable<DeviceMonitoringCalendarAssignment> assignments,
        CancellationToken cancellationToken = default)
    {
        await using var connection = await _connectionFactory.CreateOpenConnectionAsync();
        using var transaction = connection.BeginTransaction();
        await ClearDefaultIfNeededAsync(connection, transaction, calendar.IsDefault, cancellationToken);

        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            UPDATE MonitoringCalendars
            SET Name = @Name,
                TimezoneId = @TimezoneId,
                IsDefault = @IsDefault,
                UpdatedAtUtc = @UpdatedAtUtc
            WHERE Id = @Id;
            """;
        AddCalendarParameters(command, calendar);
        AddParameter(command, "@Id", calendar.Id);
        await command.ExecuteNonQueryAsync(cancellationToken);
        await ReplaceRulesAsync(connection, transaction, calendar.Id, rules, cancellationToken);
        await ReplaceAssignmentsAsync(connection, transaction, calendar.Id, assignments, cancellationToken);
        transaction.Commit();
    }

    public async Task DeleteAsync(long id, CancellationToken cancellationToken = default)
    {
        await using var connection = await _connectionFactory.CreateOpenConnectionAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = "DELETE FROM MonitoringCalendars WHERE Id = @Id AND IsDefault = 0;";
        AddParameter(command, "@Id", id);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<MonitoringCalendarListItem>> GetAllAsync(CancellationToken cancellationToken = default)
    {
        var calendars = new Dictionary<long, MonitoringCalendarListItemBuilder>();
        await using var connection = await _connectionFactory.CreateOpenConnectionAsync();

        await using (var command = connection.CreateCommand())
        {
            command.CommandText = """
                SELECT Id, Name, TimezoneId, IsDefault, CreatedAtUtc, UpdatedAtUtc
                FROM MonitoringCalendars
                ORDER BY IsDefault DESC, Name COLLATE NOCASE;
                """;
            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
            {
                var calendar = new MonitoringCalendar
                {
                    Id = reader.GetInt64(0),
                    Name = reader.GetString(1),
                    TimezoneId = reader.GetString(2),
                    IsDefault = reader.GetInt32(3) == 1,
                    CreatedAtUtc = FromStorageDate(reader.GetString(4)),
                    UpdatedAtUtc = FromStorageDate(reader.GetString(5))
                };
                calendars[calendar.Id] = new MonitoringCalendarListItemBuilder(calendar);
            }
        }

        await using (var command = connection.CreateCommand())
        {
            command.CommandText = """
                SELECT Id, CalendarId, DayOfWeek, StartTime, EndTime, IsEnabled
                FROM MonitoringCalendarRules
                ORDER BY DayOfWeek, StartTime;
                """;
            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
            {
                var calendarId = reader.GetInt64(1);
                if (calendars.TryGetValue(calendarId, out var builder))
                {
                    builder.Rules.Add(new MonitoringCalendarRule
                    {
                        Id = reader.GetInt64(0),
                        CalendarId = calendarId,
                        DayOfWeek = (DayOfWeek)reader.GetInt32(2),
                        StartTime = TimeSpan.TryParse(reader.GetString(3), CultureInfo.InvariantCulture, out var start) ? start : TimeSpan.Zero,
                        EndTime = TimeSpan.TryParse(reader.GetString(4), CultureInfo.InvariantCulture, out var end) ? end : TimeSpan.FromHours(23).Add(TimeSpan.FromMinutes(59)),
                        IsEnabled = reader.GetInt32(5) == 1
                    });
                }
            }
        }

        await using (var command = connection.CreateCommand())
        {
            command.CommandText = """
                SELECT Id, TargetType, TargetId, CalendarId, CreatedAtUtc
                FROM DeviceMonitoringCalendarAssignments
                ORDER BY TargetType, TargetId;
                """;
            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
            {
                var calendarId = reader.GetInt64(3);
                if (calendars.TryGetValue(calendarId, out var builder))
                {
                    builder.Assignments.Add(new DeviceMonitoringCalendarAssignment
                    {
                        Id = reader.GetInt64(0),
                        TargetType = ParseTargetType(reader.GetString(1)),
                        TargetId = reader.IsDBNull(2) ? null : reader.GetInt32(2),
                        CalendarId = calendarId,
                        CreatedAtUtc = FromStorageDate(reader.GetString(4))
                    });
                }
            }
        }

        return calendars.Values.Select(builder => builder.Build()).ToList();
    }

    private static async Task ClearDefaultIfNeededAsync(SqliteConnection connection, SqliteTransaction transaction, bool setDefault, CancellationToken cancellationToken)
    {
        if (!setDefault)
        {
            return;
        }

        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = "UPDATE MonitoringCalendars SET IsDefault = 0;";
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static async Task ReplaceRulesAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        long calendarId,
        IEnumerable<MonitoringCalendarRule> rules,
        CancellationToken cancellationToken)
    {
        await using var delete = connection.CreateCommand();
        delete.Transaction = transaction;
        delete.CommandText = "DELETE FROM MonitoringCalendarRules WHERE CalendarId = @CalendarId;";
        AddParameter(delete, "@CalendarId", calendarId);
        await delete.ExecuteNonQueryAsync(cancellationToken);

        foreach (var rule in rules)
        {
            await using var insert = connection.CreateCommand();
            insert.Transaction = transaction;
            insert.CommandText = """
                INSERT INTO MonitoringCalendarRules
                    (CalendarId, DayOfWeek, StartTime, EndTime, IsEnabled)
                VALUES
                    (@CalendarId, @DayOfWeek, @StartTime, @EndTime, @IsEnabled);
                """;
            AddParameter(insert, "@CalendarId", calendarId);
            AddParameter(insert, "@DayOfWeek", (int)rule.DayOfWeek);
            AddParameter(insert, "@StartTime", rule.StartTime.ToString("c", CultureInfo.InvariantCulture));
            AddParameter(insert, "@EndTime", rule.EndTime.ToString("c", CultureInfo.InvariantCulture));
            AddParameter(insert, "@IsEnabled", rule.IsEnabled ? 1 : 0);
            await insert.ExecuteNonQueryAsync(cancellationToken);
        }
    }

    private static async Task ReplaceAssignmentsAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        long calendarId,
        IEnumerable<DeviceMonitoringCalendarAssignment> assignments,
        CancellationToken cancellationToken)
    {
        await using var delete = connection.CreateCommand();
        delete.Transaction = transaction;
        delete.CommandText = "DELETE FROM DeviceMonitoringCalendarAssignments WHERE CalendarId = @CalendarId;";
        AddParameter(delete, "@CalendarId", calendarId);
        await delete.ExecuteNonQueryAsync(cancellationToken);

        foreach (var assignment in assignments)
        {
            await using var insert = connection.CreateCommand();
            insert.Transaction = transaction;
            insert.CommandText = """
                INSERT INTO DeviceMonitoringCalendarAssignments
                    (TargetType, TargetId, CalendarId, CreatedAtUtc)
                VALUES
                    (@TargetType, @TargetId, @CalendarId, @CreatedAtUtc);
                """;
            AddParameter(insert, "@TargetType", assignment.TargetType.ToString());
            AddParameter(insert, "@TargetId", assignment.TargetId);
            AddParameter(insert, "@CalendarId", calendarId);
            AddParameter(insert, "@CreatedAtUtc", ToStorageDate(assignment.CreatedAtUtc == default ? DateTime.UtcNow : assignment.CreatedAtUtc));
            await insert.ExecuteNonQueryAsync(cancellationToken);
        }
    }

    private static void AddCalendarParameters(SqliteCommand command, MonitoringCalendar calendar)
    {
        AddParameter(command, "@Name", calendar.Name.Trim());
        AddParameter(command, "@TimezoneId", string.IsNullOrWhiteSpace(calendar.TimezoneId) ? TimeZoneInfo.Local.Id : calendar.TimezoneId);
        AddParameter(command, "@IsDefault", calendar.IsDefault ? 1 : 0);
        AddParameter(command, "@CreatedAtUtc", ToStorageDate(calendar.CreatedAtUtc == default ? DateTime.UtcNow : calendar.CreatedAtUtc));
        AddParameter(command, "@UpdatedAtUtc", ToStorageDate(calendar.UpdatedAtUtc == default ? DateTime.UtcNow : calendar.UpdatedAtUtc));
    }

    private static MonitoringTargetType ParseTargetType(string value)
    {
        return Enum.TryParse<MonitoringTargetType>(value, true, out var parsed)
            ? parsed
            : MonitoringTargetType.AllDevices;
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
        return DateTime.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var parsed)
            ? parsed.ToUniversalTime()
            : DateTime.UtcNow;
    }

    private sealed class MonitoringCalendarListItemBuilder
    {
        public MonitoringCalendarListItemBuilder(MonitoringCalendar calendar)
        {
            Calendar = calendar;
        }

        public MonitoringCalendar Calendar { get; }

        public List<MonitoringCalendarRule> Rules { get; } = new();

        public List<DeviceMonitoringCalendarAssignment> Assignments { get; } = new();

        public MonitoringCalendarListItem Build()
        {
            return new MonitoringCalendarListItem
            {
                Calendar = Calendar,
                Rules = Rules,
                Assignments = Assignments
            };
        }
    }
}
