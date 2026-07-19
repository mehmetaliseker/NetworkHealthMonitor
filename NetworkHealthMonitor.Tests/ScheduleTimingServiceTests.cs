using Microsoft.Data.Sqlite;
using NetworkHealthMonitor.Data;
using NetworkHealthMonitor.Models;
using NetworkHealthMonitor.Services;
using Xunit;

namespace NetworkHealthMonitor.Tests;

public sealed class ScheduleTimingServiceTests
{
    private readonly ScheduleTimingService _timing = new();

    [Fact]
    public void Times_per_day_four_generates_four_evenly_distributed_daily_times()
    {
        var plan = CreatePlan(ScheduleMode.TimesPerDay);
        plan.TimesPerDay = 4;
        plan.DailyTimes = string.Empty;

        var next = _timing.GetNextOccurrences(plan, new DateTime(2026, 7, 18, 23, 59, 0, DateTimeKind.Utc), 4);

        Assert.Equal(
            new[]
            {
                new DateTime(2026, 7, 19, 0, 0, 0, DateTimeKind.Utc),
                new DateTime(2026, 7, 19, 6, 0, 0, DateTimeKind.Utc),
                new DateTime(2026, 7, 19, 12, 0, 0, DateTimeKind.Utc),
                new DateTime(2026, 7, 19, 18, 0, 0, DateTimeKind.Utc)
            },
            next);
    }

    [Fact]
    public void Daily_custom_times_are_sorted_and_used_for_next_runs()
    {
        var plan = CreatePlan(ScheduleMode.DailyTimes);
        plan.DailyTimes = "20:00;08:00;12:00;16:00";

        var next = _timing.GetNextOccurrences(plan, new DateTime(2026, 7, 19, 7, 59, 0, DateTimeKind.Utc), 4);

        Assert.Equal(
            new[]
            {
                new DateTime(2026, 7, 19, 8, 0, 0, DateTimeKind.Utc),
                new DateTime(2026, 7, 19, 12, 0, 0, DateTimeKind.Utc),
                new DateTime(2026, 7, 19, 16, 0, 0, DateTimeKind.Utc),
                new DateTime(2026, 7, 19, 20, 0, 0, DateTimeKind.Utc)
            },
            next);
    }

    [Theory]
    [InlineData(20, ScheduleIntervalUnit.Minutes, 20)]
    [InlineData(7, ScheduleIntervalUnit.Days, 7 * 24 * 60)]
    [InlineData(30, ScheduleIntervalUnit.Days, 30 * 24 * 60)]
    [InlineData(52, ScheduleIntervalUnit.Weeks, 52 * 7 * 24 * 60)]
    public void Fixed_interval_calculates_expected_minutes(int value, ScheduleIntervalUnit unit, int expectedMinutes)
    {
        var plan = CreatePlan(ScheduleMode.FixedInterval);
        plan.IntervalValue = value;
        plan.IntervalUnit = unit;
        var from = new DateTime(2026, 7, 19, 10, 0, 0, DateTimeKind.Utc);

        var next = _timing.CalculateNextRunAfterExecution(plan, from);

        Assert.Equal(from.AddMinutes(expectedMinutes), next);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void Invalid_zero_or_negative_fixed_interval_is_rejected(int value)
    {
        var plan = CreatePlan(ScheduleMode.FixedInterval);
        plan.IntervalValue = value;

        var result = _timing.Validate(plan);

        Assert.False(result.Success);
    }

    [Fact]
    public void Maximum_365_day_normal_interval_is_allowed()
    {
        var plan = CreatePlan(ScheduleMode.FixedInterval);
        plan.IntervalValue = 365;
        plan.IntervalUnit = ScheduleIntervalUnit.Days;

        Assert.True(_timing.Validate(plan).Success);
    }

    [Fact]
    public void More_than_365_day_normal_interval_is_rejected()
    {
        var plan = CreatePlan(ScheduleMode.FixedInterval);
        plan.IntervalValue = 366;
        plan.IntervalUnit = ScheduleIntervalUnit.Days;

        Assert.False(_timing.Validate(plan).Success);
    }

    [Fact]
    public void Duplicate_daily_time_is_rejected()
    {
        var plan = CreatePlan(ScheduleMode.DailyTimes);
        plan.DailyTimes = "08:00;12:00;08:00";

        Assert.False(_timing.Validate(plan).Success);
    }

    [Fact]
    public void Weekly_schedule_runs_only_on_selected_days()
    {
        var plan = CreatePlan(ScheduleMode.Weekly);
        plan.SelectedWeekDays = "Monday,Wednesday";
        plan.DailyTimes = "08:00";

        var next = _timing.GetNextOccurrences(plan, new DateTime(2026, 7, 20, 8, 1, 0, DateTimeKind.Utc), 2);

        Assert.Equal(new DateTime(2026, 7, 22, 8, 0, 0, DateTimeKind.Utc), next[0]);
        Assert.Equal(new DateTime(2026, 7, 27, 8, 0, 0, DateTimeKind.Utc), next[1]);
    }

    [Fact]
    public void Weekly_schedule_without_day_is_rejected()
    {
        var plan = CreatePlan(ScheduleMode.Weekly);
        plan.DailyTimes = "08:00";

        Assert.False(_timing.Validate(plan).Success);
    }

    [Fact]
    public void Offline_device_uses_offline_recheck_interval_instead_of_normal_daily_plan()
    {
        var now = new DateTime(2026, 7, 19, 10, 20, 0, DateTimeKind.Utc);
        var device = CreateDevice(now.AddMinutes(-20), DeviceStatus.Offline, 5);
        var plan = CreatePlan(ScheduleMode.TimesPerDay);
        plan.TimesPerDay = 4;
        plan.OfflineRecheckIntervalSeconds = 20 * 60;
        var policy = new DeviceCheckPolicyService().ResolvePolicy(device, null, plan, new AppSettings(), plan.ToPingOptions());

        Assert.True(new DeviceCheckPolicyService().IsDue(device, policy, now));
        Assert.Equal(now, policyDeviceNext(device, policy));

        static DateTime? policyDeviceNext(Device device, DeviceCheckPolicy policy)
        {
            return new DeviceCheckPolicyService().GetNextCheckAt(device, policy);
        }
    }

    [Fact]
    public void Online_device_uses_normal_plan_interval()
    {
        var now = new DateTime(2026, 7, 19, 10, 20, 0, DateTimeKind.Utc);
        var device = CreateDevice(now.AddMinutes(-20), DeviceStatus.Online, 0);
        var plan = CreatePlan(ScheduleMode.TimesPerDay);
        plan.TimesPerDay = 4;
        var policyService = new DeviceCheckPolicyService();
        var policy = policyService.ResolvePolicy(device, null, plan, new AppSettings(), plan.ToPingOptions());

        Assert.False(policyService.IsDue(device, policy, now));
        Assert.Equal(now.AddHours(5).AddMinutes(40), policyService.GetNextCheckAt(device, policy));
    }

    [Fact]
    public void Confirmation_retry_runs_before_incident_confirmation()
    {
        var now = new DateTime(2026, 7, 19, 10, 1, 0, DateTimeKind.Utc);
        var device = CreateDevice(now.AddMinutes(-1), DeviceStatus.Warning, 1);
        var plan = CreatePlan(ScheduleMode.FixedInterval);
        plan.ConfirmationRetryCount = 3;
        plan.ConfirmationRetryIntervalSeconds = 60;
        var policyService = new DeviceCheckPolicyService();
        var policy = policyService.ResolvePolicy(device, null, plan, new AppSettings(), plan.ToPingOptions());

        Assert.True(policyService.IsDue(device, policy, now));
    }

    [Fact]
    public void Catch_up_due_plan_executes_once_then_moves_to_future()
    {
        var plan = CreatePlan(ScheduleMode.FixedInterval);
        plan.IntervalValue = 1;
        plan.IntervalUnit = ScheduleIntervalUnit.Days;
        plan.NextRunAt = new DateTime(2026, 7, 1, 8, 0, 0, DateTimeKind.Utc);
        plan.MissedRunPolicy = MissedRunPolicy.SingleCatchUp;
        var now = new DateTime(2026, 7, 19, 8, 0, 0, DateTimeKind.Utc);

        Assert.True(_timing.IsDue(plan, now));
        Assert.Equal(now.AddDays(1), _timing.CalculateNextRunAfterExecution(plan, now));
    }

    [Fact]
    public async Task Migration_preserves_legacy_schedule_interval_as_fixed_interval()
    {
        var root = Path.Combine(Path.GetTempPath(), "nhm-scheduler-migration-" + Guid.NewGuid().ToString("N"));
        var programData = Path.Combine(root, "programdata");
        var legacy = Path.Combine(root, "legacy");
        try
        {
            DatabasePaths.Configure(new FixedApplicationPathProvider(programData), legacy);
            Directory.CreateDirectory(Path.GetDirectoryName(DatabasePaths.DatabaseFilePath)!);
            await using (var connection = new SqliteConnection($"Data Source={DatabasePaths.DatabaseFilePath}"))
            {
                await connection.OpenAsync();
                await ExecuteAsync(connection, """
                    CREATE TABLE SchemaMigrations (
                        Version TEXT PRIMARY KEY,
                        AppliedAtUtc TEXT NOT NULL
                    );
                    """);
                await ExecuteAsync(connection, """
                    INSERT INTO SchemaMigrations (Version, AppliedAtUtc)
                    VALUES ('2026071501-core-server-schema', '2026-07-15T00:00:00.0000000Z');
                    """);
                await ExecuteAsync(connection, """
                    CREATE TABLE SchedulePlans (
                        Id INTEGER PRIMARY KEY AUTOINCREMENT,
                        Name TEXT NOT NULL,
                        TargetType TEXT NOT NULL,
                        TargetValue TEXT NOT NULL DEFAULT '',
                        IntervalMinutes INTEGER NOT NULL DEFAULT 15,
                        TimeoutMs INTEGER NOT NULL DEFAULT 1000,
                        MaxParallelism INTEGER NOT NULL DEFAULT 4,
                        FailureThreshold INTEGER NOT NULL DEFAULT 3,
                        IsActive INTEGER NOT NULL DEFAULT 1,
                        Description TEXT NOT NULL DEFAULT '',
                        LastRunAt TEXT NULL,
                        NextRunAt TEXT NULL,
                        LastStatus TEXT NOT NULL DEFAULT '',
                        CreatedAt TEXT NOT NULL,
                        UpdatedAt TEXT NOT NULL
                    );
                    """);
                await ExecuteAsync(connection, """
                    INSERT INTO SchedulePlans
                        (Name, TargetType, TargetValue, IntervalMinutes, TimeoutMs, MaxParallelism, FailureThreshold, IsActive, Description, CreatedAt, UpdatedAt)
                    VALUES
                        ('Legacy 6 hours', 'AllDevices', '', 360, 1000, 4, 3, 1, '', '2026-07-15T00:00:00.0000000Z', '2026-07-15T00:00:00.0000000Z');
                    """);
            }

            var factory = new SqliteConnectionFactory();
            await factory.InitializeAsync();
            await factory.InitializeAsync();
            var plans = await new SchedulePlanRepository(factory).GetAllAsync();

            Assert.Single(plans);
            Assert.Equal(ScheduleMode.FixedInterval, plans[0].ScheduleMode);
            Assert.Equal(360, plans[0].IntervalValue);
            Assert.Equal(ScheduleIntervalUnit.Minutes, plans[0].IntervalUnit);
            Assert.Equal(360, plans[0].IntervalMinutes);
            await using var verify = await factory.CreateOpenConnectionAsync();
            await using var command = verify.CreateCommand();
            command.CommandText = "SELECT COUNT(1) FROM SchemaMigrations WHERE Version = @Version;";
            command.Parameters.AddWithValue("@Version", SqliteConnectionFactory.ExtendedSchedulerSchemaMigrationId);
            Assert.Equal(1, Convert.ToInt32(await command.ExecuteScalarAsync()));
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
    }

    private static SchedulePlan CreatePlan(ScheduleMode mode)
    {
        return new SchedulePlan
        {
            Name = "Test plan",
            TargetType = SchedulePlanTargetType.AllDevices,
            ScheduleMode = mode,
            IntervalValue = 20,
            IntervalUnit = ScheduleIntervalUnit.Minutes,
            TimesPerDay = 1,
            TimeZoneId = TimeZoneInfo.Utc.Id,
            FailureRetryEnabled = true,
            ConfirmationRetryCount = 3,
            ConfirmationRetryIntervalSeconds = 60,
            OfflineRecheckIntervalSeconds = 20 * 60,
            TimeoutMs = 1000,
            MaxParallelism = 4,
            FailureThreshold = 1,
            IsActive = true
        };
    }

    private static Device CreateDevice(DateTime lastCheckedAtUtc, DeviceStatus status, int failures)
    {
        return new Device
        {
            Id = 1,
            Name = "Test device",
            IpAddress = "192.0.2.10",
            LastCheckedAt = lastCheckedAtUtc,
            LastStatus = status,
            ConsecutiveFailures = failures,
            AutoCheckEnabled = true,
            IsActive = true,
            IsEnabled = true
        };
    }

    private static async Task ExecuteAsync(SqliteConnection connection, string commandText)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = commandText;
        await command.ExecuteNonQueryAsync();
    }
}
