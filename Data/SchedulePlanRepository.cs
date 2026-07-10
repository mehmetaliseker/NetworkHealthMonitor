using System.Globalization;
using Microsoft.Data.Sqlite;
using NetworkHealthMonitor.Models;

namespace NetworkHealthMonitor.Data;

public sealed class SchedulePlanRepository
{
    private readonly SqliteConnectionFactory _connectionFactory;

    public SchedulePlanRepository(SqliteConnectionFactory connectionFactory)
    {
        _connectionFactory = connectionFactory;
    }

    public async Task<IReadOnlyList<SchedulePlan>> GetAllAsync()
    {
        var plans = new List<SchedulePlan>();
        await using var connection = await _connectionFactory.CreateOpenConnectionAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT Id, Name, TargetType, TargetValue, IntervalMinutes, TimeoutMs, MaxParallelism,
                   FailureThreshold, IsActive, Description, LastRunAt, CreatedAt, UpdatedAt
            FROM SchedulePlans
            ORDER BY IsActive DESC, Name COLLATE NOCASE;
            """;

        await using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            plans.Add(ReadPlan(reader));
        }

        return plans;
    }

    public async Task<IReadOnlyList<SchedulePlan>> GetActiveAsync()
    {
        var plans = new List<SchedulePlan>();
        await using var connection = await _connectionFactory.CreateOpenConnectionAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT Id, Name, TargetType, TargetValue, IntervalMinutes, TimeoutMs, MaxParallelism,
                   FailureThreshold, IsActive, Description, LastRunAt, CreatedAt, UpdatedAt
            FROM SchedulePlans
            WHERE IsActive = 1
            ORDER BY Name COLLATE NOCASE;
            """;

        await using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            plans.Add(ReadPlan(reader));
        }

        return plans;
    }

    public async Task<int> AddAsync(SchedulePlan plan)
    {
        var now = DateTime.Now;
        plan.CreatedAt = now;
        plan.UpdatedAt = now;

        await using var connection = await _connectionFactory.CreateOpenConnectionAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO SchedulePlans
                (Name, TargetType, TargetValue, IntervalMinutes, TimeoutMs, MaxParallelism,
                 FailureThreshold, IsActive, Description, LastRunAt, CreatedAt, UpdatedAt)
            VALUES
                (@Name, @TargetType, @TargetValue, @IntervalMinutes, @TimeoutMs, @MaxParallelism,
                 @FailureThreshold, @IsActive, @Description, @LastRunAt, @CreatedAt, @UpdatedAt);
            SELECT last_insert_rowid();
            """;

        AddPlanParameters(command, plan);
        var result = await command.ExecuteScalarAsync();
        plan.Id = Convert.ToInt32(result, CultureInfo.InvariantCulture);
        return plan.Id;
    }

    public async Task UpdateAsync(SchedulePlan plan)
    {
        plan.UpdatedAt = DateTime.Now;
        await using var connection = await _connectionFactory.CreateOpenConnectionAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = """
            UPDATE SchedulePlans
            SET Name = @Name,
                TargetType = @TargetType,
                TargetValue = @TargetValue,
                IntervalMinutes = @IntervalMinutes,
                TimeoutMs = @TimeoutMs,
                MaxParallelism = @MaxParallelism,
                FailureThreshold = @FailureThreshold,
                IsActive = @IsActive,
                Description = @Description,
                LastRunAt = @LastRunAt,
                UpdatedAt = @UpdatedAt
            WHERE Id = @Id;
            """;

        AddPlanParameters(command, plan);
        AddParameter(command, "@Id", plan.Id);
        await command.ExecuteNonQueryAsync();
    }

    public async Task DeleteAsync(int id)
    {
        await using var connection = await _connectionFactory.CreateOpenConnectionAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = "DELETE FROM SchedulePlans WHERE Id = @Id;";
        AddParameter(command, "@Id", id);
        await command.ExecuteNonQueryAsync();
    }

    public async Task UpdateLastRunAsync(int id, DateTime lastRunAt)
    {
        await using var connection = await _connectionFactory.CreateOpenConnectionAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = """
            UPDATE SchedulePlans
            SET LastRunAt = @LastRunAt,
                UpdatedAt = @UpdatedAt
            WHERE Id = @Id;
            """;
        AddParameter(command, "@LastRunAt", ToStorageDate(lastRunAt));
        AddParameter(command, "@UpdatedAt", ToStorageDate(DateTime.Now));
        AddParameter(command, "@Id", id);
        await command.ExecuteNonQueryAsync();
    }

    public async Task<bool> ExistsByNameAsync(string name, int? excludeId = null)
    {
        await using var connection = await _connectionFactory.CreateOpenConnectionAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT COUNT(1)
            FROM SchedulePlans
            WHERE Name = @Name
              AND (@ExcludeId IS NULL OR Id <> @ExcludeId);
            """;
        AddParameter(command, "@Name", name.Trim());
        AddParameter(command, "@ExcludeId", excludeId);
        var result = await command.ExecuteScalarAsync();
        return Convert.ToInt32(result, CultureInfo.InvariantCulture) > 0;
    }

    private static SchedulePlan ReadPlan(SqliteDataReader reader)
    {
        return new SchedulePlan
        {
            Id = reader.GetInt32(0),
            Name = reader.GetString(1),
            TargetType = SchedulePlanTargetTypeExtensions.FromStorageValue(reader.GetString(2)),
            TargetValue = reader.GetString(3),
            IntervalMinutes = reader.GetInt32(4),
            TimeoutMs = reader.GetInt32(5),
            MaxParallelism = reader.GetInt32(6),
            FailureThreshold = reader.GetInt32(7),
            IsActive = reader.GetInt32(8) == 1,
            Description = reader.GetString(9),
            LastRunAt = reader.IsDBNull(10) ? null : FromStorageDate(reader.GetString(10)),
            CreatedAt = FromStorageDate(reader.GetString(11)),
            UpdatedAt = FromStorageDate(reader.GetString(12))
        };
    }

    private static void AddPlanParameters(SqliteCommand command, SchedulePlan plan)
    {
        AddParameter(command, "@Name", plan.Name.Trim());
        AddParameter(command, "@TargetType", plan.TargetType.ToStorageValue());
        AddParameter(command, "@TargetValue", plan.TargetValue.Trim());
        AddParameter(command, "@IntervalMinutes", Math.Max(1, plan.IntervalMinutes));
        AddParameter(command, "@TimeoutMs", Math.Clamp(plan.TimeoutMs, AppSettings.MinPingTimeoutMs, AppSettings.MaxPingTimeoutMs));
        AddParameter(command, "@MaxParallelism", Math.Clamp(plan.MaxParallelism, AppSettings.MinParallelPings, AppSettings.MaxParallelPingsLimit));
        AddParameter(command, "@FailureThreshold", Math.Clamp(plan.FailureThreshold, AppSettings.MinFailureThreshold, AppSettings.MaxFailureThreshold));
        AddParameter(command, "@IsActive", plan.IsActive ? 1 : 0);
        AddParameter(command, "@Description", plan.Description);
        AddParameter(command, "@LastRunAt", plan.LastRunAt.HasValue ? ToStorageDate(plan.LastRunAt.Value) : null);
        AddParameter(command, "@CreatedAt", ToStorageDate(plan.CreatedAt));
        AddParameter(command, "@UpdatedAt", ToStorageDate(plan.UpdatedAt));
    }

    private static void AddParameter(SqliteCommand command, string name, object? value)
    {
        command.Parameters.AddWithValue(name, value ?? DBNull.Value);
    }

    private static string ToStorageDate(DateTime value)
    {
        return value.ToString("O", CultureInfo.InvariantCulture);
    }

    private static DateTime FromStorageDate(string value)
    {
        return DateTime.TryParse(
            value,
            CultureInfo.InvariantCulture,
            DateTimeStyles.RoundtripKind,
            out var parsed)
            ? parsed
            : DateTime.Now;
    }
}
