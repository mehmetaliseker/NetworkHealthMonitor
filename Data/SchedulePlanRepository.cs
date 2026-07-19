using System.Globalization;
using Microsoft.Data.Sqlite;
using NetworkHealthMonitor.Models;

namespace NetworkHealthMonitor.Data;

public sealed class SchedulePlanRepository
{
    private const string SchedulePlanSelectColumns = """
        Id, Name, TargetType, TargetValue, IntervalMinutes, TimeoutMs, MaxParallelism,
        FailureThreshold, IsActive, Description, LastRunAt, NextRunAt, LastStatus, CreatedAt, UpdatedAt,
        ScheduleMode, IntervalValue, IntervalUnit, TimesPerDay, DailyTimes, SelectedWeekDays, TimeZoneId,
        FailureRetryEnabled, ConfirmationRetryCount, ConfirmationRetryIntervalSeconds, OfflineRecheckIntervalSeconds,
        MissedRunPolicy
        """;

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
        command.CommandText = $"""
            SELECT {SchedulePlanSelectColumns}
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
        command.CommandText = $"""
            SELECT {SchedulePlanSelectColumns}
            FROM SchedulePlans
            WHERE IsActive = 1
            ORDER BY COALESCE(NextRunAt, LastRunAt, CreatedAt), Name COLLATE NOCASE;
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
        var now = DateTime.UtcNow;
        plan.CreatedAt = now;
        plan.UpdatedAt = now;

        await using var connection = await _connectionFactory.CreateOpenConnectionAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO SchedulePlans
                (Name, TargetType, TargetValue, IntervalMinutes, TimeoutMs, MaxParallelism,
                 FailureThreshold, IsActive, Description, LastRunAt, NextRunAt, LastStatus, CreatedAt, UpdatedAt,
                 ScheduleMode, IntervalValue, IntervalUnit, TimesPerDay, DailyTimes, SelectedWeekDays, TimeZoneId,
                 FailureRetryEnabled, ConfirmationRetryCount, ConfirmationRetryIntervalSeconds, OfflineRecheckIntervalSeconds,
                 MissedRunPolicy)
            VALUES
                (@Name, @TargetType, @TargetValue, @IntervalMinutes, @TimeoutMs, @MaxParallelism,
                 @FailureThreshold, @IsActive, @Description, @LastRunAt, @NextRunAt, @LastStatus, @CreatedAt, @UpdatedAt,
                 @ScheduleMode, @IntervalValue, @IntervalUnit, @TimesPerDay, @DailyTimes, @SelectedWeekDays, @TimeZoneId,
                 @FailureRetryEnabled, @ConfirmationRetryCount, @ConfirmationRetryIntervalSeconds, @OfflineRecheckIntervalSeconds,
                 @MissedRunPolicy);
            SELECT last_insert_rowid();
            """;

        AddPlanParameters(command, plan);
        var result = await command.ExecuteScalarAsync();
        plan.Id = Convert.ToInt32(result, CultureInfo.InvariantCulture);
        return plan.Id;
    }

    public async Task UpdateAsync(SchedulePlan plan)
    {
        plan.UpdatedAt = DateTime.UtcNow;
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
                NextRunAt = @NextRunAt,
                LastStatus = @LastStatus,
                ScheduleMode = @ScheduleMode,
                IntervalValue = @IntervalValue,
                IntervalUnit = @IntervalUnit,
                TimesPerDay = @TimesPerDay,
                DailyTimes = @DailyTimes,
                SelectedWeekDays = @SelectedWeekDays,
                TimeZoneId = @TimeZoneId,
                FailureRetryEnabled = @FailureRetryEnabled,
                ConfirmationRetryCount = @ConfirmationRetryCount,
                ConfirmationRetryIntervalSeconds = @ConfirmationRetryIntervalSeconds,
                OfflineRecheckIntervalSeconds = @OfflineRecheckIntervalSeconds,
                MissedRunPolicy = @MissedRunPolicy,
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
        await UpdateRunStateAsync(id, lastRunAt, lastRunAt.AddMinutes(AppSettings.DefaultSchedulePlanIntervalMinutes), string.Empty);
    }

    public async Task UpdateRunStateAsync(int id, DateTime lastRunAt, DateTime? nextRunAt, string lastStatus)
    {
        await using var connection = await _connectionFactory.CreateOpenConnectionAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = """
            UPDATE SchedulePlans
            SET LastRunAt = @LastRunAt,
                NextRunAt = @NextRunAt,
                LastStatus = @LastStatus,
                UpdatedAt = @UpdatedAt
            WHERE Id = @Id;
            """;
        AddParameter(command, "@LastRunAt", ToStorageDate(lastRunAt));
        AddParameter(command, "@NextRunAt", nextRunAt.HasValue ? ToStorageDate(nextRunAt.Value) : null);
        AddParameter(command, "@LastStatus", lastStatus);
        AddParameter(command, "@UpdatedAt", ToStorageDate(DateTime.UtcNow));
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
            NextRunAt = reader.IsDBNull(11) ? null : FromStorageDate(reader.GetString(11)),
            LastStatus = reader.GetString(12),
            CreatedAt = FromStorageDate(reader.GetString(13)),
            UpdatedAt = FromStorageDate(reader.GetString(14)),
            ScheduleMode = ScheduleModelExtensions.ScheduleModeFromStorage(reader.GetString(15)),
            IntervalValue = reader.GetInt32(16),
            IntervalUnit = ScheduleModelExtensions.ScheduleIntervalUnitFromStorage(reader.GetString(17)),
            TimesPerDay = reader.GetInt32(18),
            DailyTimes = reader.GetString(19),
            SelectedWeekDays = reader.GetString(20),
            TimeZoneId = reader.GetString(21),
            FailureRetryEnabled = reader.GetInt32(22) == 1,
            ConfirmationRetryCount = reader.GetInt32(23),
            ConfirmationRetryIntervalSeconds = reader.GetInt32(24),
            OfflineRecheckIntervalSeconds = reader.GetInt32(25),
            MissedRunPolicy = ScheduleModelExtensions.MissedRunPolicyFromStorage(reader.GetString(26))
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
        AddParameter(command, "@NextRunAt", plan.NextRunAt.HasValue ? ToStorageDate(plan.NextRunAt.Value) : null);
        AddParameter(command, "@LastStatus", plan.LastStatus);
        AddParameter(command, "@CreatedAt", ToStorageDate(plan.CreatedAt));
        AddParameter(command, "@UpdatedAt", ToStorageDate(plan.UpdatedAt));
        AddParameter(command, "@ScheduleMode", plan.ScheduleMode.ToStorageValue());
        AddParameter(command, "@IntervalValue", Math.Max(1, plan.IntervalValue));
        AddParameter(command, "@IntervalUnit", plan.IntervalUnit.ToStorageValue());
        AddParameter(command, "@TimesPerDay", Math.Clamp(plan.TimesPerDay, 0, 48));
        AddParameter(command, "@DailyTimes", plan.DailyTimes);
        AddParameter(command, "@SelectedWeekDays", plan.SelectedWeekDays);
        AddParameter(command, "@TimeZoneId", string.IsNullOrWhiteSpace(plan.TimeZoneId) ? TimeZoneInfo.Local.Id : plan.TimeZoneId);
        AddParameter(command, "@FailureRetryEnabled", plan.FailureRetryEnabled ? 1 : 0);
        AddParameter(command, "@ConfirmationRetryCount", Math.Clamp(plan.ConfirmationRetryCount, AppSettings.MinConfirmationRetryCount, AppSettings.MaxConfirmationRetryCount));
        AddParameter(command, "@ConfirmationRetryIntervalSeconds", Math.Clamp(plan.ConfirmationRetryIntervalSeconds, AppSettings.MinConfirmationRetryIntervalSeconds, AppSettings.MaxConfirmationRetryIntervalSeconds));
        AddParameter(command, "@OfflineRecheckIntervalSeconds", Math.Clamp(plan.OfflineRecheckIntervalSeconds, AppSettings.MinOfflineRecheckIntervalSeconds, AppSettings.MaxOfflineRecheckIntervalSeconds));
        AddParameter(command, "@MissedRunPolicy", plan.MissedRunPolicy.ToStorageValue());
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
