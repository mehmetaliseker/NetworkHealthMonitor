using System.Globalization;
using System.Text.Json;
using Microsoft.Data.Sqlite;
using NetworkHealthMonitor.Data;
using NetworkHealthMonitor.Models;

namespace NetworkHealthMonitor.Worker;

public static class DatabaseVerificationCommand
{
    private static readonly string[] CountedTables =
    [
        "Devices",
        "DeviceGroups",
        "SchedulePlans",
        "PingLogs",
        "DeviceIncidents",
        "NotificationOutbox",
        "AppSettings"
    ];

    private static readonly string[] ExpectedMigrations =
    [
        SqliteConnectionFactory.CoreServerSchemaMigrationId,
        DatabaseMigrationRunner.DeviceIncidentsEndedAtUtcMigrationId,
        SqliteConnectionFactory.NotificationEmailSuppressionSchemaMigrationId,
        SqliteConnectionFactory.ExtendedSchedulerSchemaMigrationId
    ];

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true
    };

    public static async Task<int> RunAsync(WorkerOptions options, CancellationToken cancellationToken = default)
    {
        DatabasePaths.Configure(options.PathProvider, options.LegacyDataDirectory);
        var report = new DatabaseVerificationReport
        {
            Mode = options.DatabaseVerificationMode.ToString(),
            DataRoot = DatabasePaths.RootDirectory,
            DatabasePath = DatabasePaths.DatabaseFilePath,
            SettingsPath = DatabasePaths.SettingsFilePath,
            SettingsFileExists = File.Exists(DatabasePaths.SettingsFilePath),
            GeneratedAtUtc = DateTime.UtcNow.ToString("O", CultureInfo.InvariantCulture),
            ExpectedMigrations = ExpectedMigrations
        };

        try
        {
            var shouldInitialize = options.DatabaseVerificationMode == DatabaseVerificationMode.VerifyDatabase;
            if (!File.Exists(DatabasePaths.DatabaseFilePath) && !shouldInitialize)
            {
                report.Errors.Add($"SQLite DB yok: {DatabasePaths.DatabaseFilePath}");
                return await CompleteAsync(report, options, cancellationToken);
            }

            if (shouldInitialize)
            {
                var initializer = new SqliteConnectionFactory();
                await initializer.InitializeAsync(cancellationToken);
                report.InitializedByCommand = true;
            }

            await using var connection = await OpenReadOnlyConnectionAsync(cancellationToken);
            report.DatabaseOpened = true;
            report.IntegrityCheck = await ExecuteScalarStringAsync(connection, "PRAGMA integrity_check;", cancellationToken);
            report.ForeignKeyCheck = await GetFirstForeignKeyErrorAsync(connection, cancellationToken) ?? "ok";
            report.Migrations.AddRange(await ReadMigrationsAsync(connection, cancellationToken));
            report.MissingExpectedMigrations = ExpectedMigrations
                .Where(expected => !report.Migrations.Any(actual => string.Equals(actual.Version, expected, StringComparison.OrdinalIgnoreCase)))
                .ToArray();

            foreach (var tableName in CountedTables)
            {
                var table = await ReadTableSummaryAsync(connection, tableName, cancellationToken);
                report.Tables[tableName] = table;
            }

            report.OpenIncidentDuplicateCount = await CountDuplicateOpenIncidentsAsync(connection, cancellationToken);
            report.OutboxIdempotencyDuplicateCount = await CountDuplicateOutboxIdempotencyKeysAsync(connection, cancellationToken);

            if (!string.Equals(report.IntegrityCheck, "ok", StringComparison.OrdinalIgnoreCase))
            {
                report.Errors.Add($"SQLite integrity_check basarisiz: {report.IntegrityCheck}");
            }

            if (!string.Equals(report.ForeignKeyCheck, "ok", StringComparison.OrdinalIgnoreCase))
            {
                report.Errors.Add($"Foreign key check basarisiz: {report.ForeignKeyCheck}");
            }

            if (options.DatabaseVerificationMode == DatabaseVerificationMode.VerifyDatabase)
            {
                await DatabaseSchemaContract.VerifyAsync(connection, cancellationToken);
                report.SchemaContractOk = true;

                if (report.MissingExpectedMigrations.Length > 0)
                {
                    report.Errors.Add("Beklenen migration eksik: " + string.Join(", ", report.MissingExpectedMigrations));
                }

                if (!report.Tables.TryGetValue("AppSettings", out var appSettings) || !appSettings.Exists)
                {
                    report.Errors.Add("AppSettings tablosu yok.");
                }
            }

            if (report.OpenIncidentDuplicateCount > 0)
            {
                report.Errors.Add($"Ayni cihaz icin birden fazla acik incident var: {report.OpenIncidentDuplicateCount}");
            }

            if (report.OutboxIdempotencyDuplicateCount > 0)
            {
                report.Errors.Add($"Duplicate outbox idempotency key var: {report.OutboxIdempotencyDuplicateCount}");
            }
        }
        catch (Exception ex)
        {
            report.Errors.Add(ex.Message);
        }

        return await CompleteAsync(report, options, cancellationToken);
    }

    private static async Task<int> CompleteAsync(
        DatabaseVerificationReport report,
        WorkerOptions options,
        CancellationToken cancellationToken)
    {
        report.Status = report.Errors.Count == 0 ? "PASS" : "FAIL";
        var json = JsonSerializer.Serialize(report, JsonOptions);
        var text = RenderText(report);

        if (!string.IsNullOrWhiteSpace(options.DatabaseReportJsonPath))
        {
            await WriteTextFileAsync(options.DatabaseReportJsonPath, json, cancellationToken);
        }

        if (!string.IsNullOrWhiteSpace(options.DatabaseReportTextPath))
        {
            await WriteTextFileAsync(options.DatabaseReportTextPath, text, cancellationToken);
        }

        Console.WriteLine(text);
        return report.Status == "PASS" ? 0 : 1;
    }

    private static async Task WriteTextFileAsync(string path, string content, CancellationToken cancellationToken)
    {
        var directory = Path.GetDirectoryName(Path.GetFullPath(path));
        if (!string.IsNullOrWhiteSpace(directory))
        {
            Directory.CreateDirectory(directory);
        }

        await File.WriteAllTextAsync(path, content, cancellationToken);
    }

    private static string RenderText(DatabaseVerificationReport report)
    {
        var lines = new List<string>
        {
            $"Status={report.Status}",
            $"Mode={report.Mode}",
            $"DatabasePath={report.DatabasePath}",
            $"DatabaseOpened={report.DatabaseOpened}",
            $"InitializedByCommand={report.InitializedByCommand}",
            $"IntegrityCheck={report.IntegrityCheck}",
            $"ForeignKeyCheck={report.ForeignKeyCheck}",
            $"SchemaContractOk={report.SchemaContractOk}",
            $"SettingsFileExists={report.SettingsFileExists}",
            $"OpenIncidentDuplicateCount={report.OpenIncidentDuplicateCount}",
            $"OutboxIdempotencyDuplicateCount={report.OutboxIdempotencyDuplicateCount}",
            $"AppliedMigrations={report.Migrations.Count}",
            $"MissingExpectedMigrations={string.Join(",", report.MissingExpectedMigrations)}"
        };

        foreach (var table in report.Tables.OrderBy(item => item.Key, StringComparer.OrdinalIgnoreCase))
        {
            var countText = table.Value.Count.HasValue
                ? table.Value.Count.Value.ToString(CultureInfo.InvariantCulture)
                : "n/a";
            lines.Add($"Table.{table.Key}.Exists={table.Value.Exists}");
            lines.Add($"Table.{table.Key}.Count={countText}");
        }

        foreach (var migration in report.Migrations)
        {
            lines.Add($"Migration={migration.Version}; AppliedAtUtc={migration.AppliedAtUtc}");
        }

        foreach (var error in report.Errors)
        {
            lines.Add("Error=" + error);
        }

        return string.Join(Environment.NewLine, lines);
    }

    private static async Task<SqliteConnection> OpenReadOnlyConnectionAsync(CancellationToken cancellationToken)
    {
        var connectionString = new SqliteConnectionStringBuilder
        {
            DataSource = DatabasePaths.DatabaseFilePath,
            Mode = SqliteOpenMode.ReadOnly,
            Pooling = false
        }.ToString();

        var connection = new SqliteConnection(connectionString);
        await connection.OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = $"PRAGMA busy_timeout = {AppSettings.SqliteBusyTimeoutMs}; PRAGMA foreign_keys = ON;";
        await command.ExecuteNonQueryAsync(cancellationToken);
        return connection;
    }

    private static async Task<string> ExecuteScalarStringAsync(
        SqliteConnection connection,
        string commandText,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = commandText;
        var value = await command.ExecuteScalarAsync(cancellationToken);
        return Convert.ToString(value, CultureInfo.InvariantCulture) ?? string.Empty;
    }

    private static async Task<string?> GetFirstForeignKeyErrorAsync(
        SqliteConnection connection,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = "PRAGMA foreign_key_check;";
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken))
        {
            return null;
        }

        return $"{reader.GetString(0)} rowid {reader.GetInt64(1)}";
    }

    private static async Task<List<DatabaseMigrationSummary>> ReadMigrationsAsync(
        SqliteConnection connection,
        CancellationToken cancellationToken)
    {
        if (!await TableExistsAsync(connection, "SchemaMigrations", cancellationToken))
        {
            return [];
        }

        var migrations = new List<DatabaseMigrationSummary>();
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT Version, AppliedAtUtc
            FROM SchemaMigrations
            ORDER BY AppliedAtUtc, Version;
            """;
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            migrations.Add(new DatabaseMigrationSummary
            {
                Version = reader.GetString(0),
                AppliedAtUtc = reader.GetString(1)
            });
        }

        return migrations;
    }

    private static async Task<DatabaseTableSummary> ReadTableSummaryAsync(
        SqliteConnection connection,
        string tableName,
        CancellationToken cancellationToken)
    {
        var exists = await TableExistsAsync(connection, tableName, cancellationToken);
        if (!exists)
        {
            return new DatabaseTableSummary { Exists = false };
        }

        await using var command = connection.CreateCommand();
        command.CommandText = $"SELECT COUNT(1) FROM {tableName};";
        var count = Convert.ToInt64(await command.ExecuteScalarAsync(cancellationToken), CultureInfo.InvariantCulture);
        return new DatabaseTableSummary
        {
            Exists = true,
            Count = count
        };
    }

    private static async Task<bool> TableExistsAsync(
        SqliteConnection connection,
        string tableName,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT COUNT(1)
            FROM sqlite_master
            WHERE type = 'table'
              AND name = @TableName;
            """;
        command.Parameters.AddWithValue("@TableName", tableName);
        var count = Convert.ToInt32(await command.ExecuteScalarAsync(cancellationToken), CultureInfo.InvariantCulture);
        return count > 0;
    }

    private static async Task<int> CountDuplicateOpenIncidentsAsync(
        SqliteConnection connection,
        CancellationToken cancellationToken)
    {
        if (!await TableExistsAsync(connection, "DeviceIncidents", cancellationToken))
        {
            return 0;
        }

        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT COUNT(1)
            FROM (
                SELECT DeviceId
                FROM DeviceIncidents
                WHERE Status = 'Open'
                GROUP BY DeviceId
                HAVING COUNT(1) > 1
            );
            """;
        return Convert.ToInt32(await command.ExecuteScalarAsync(cancellationToken), CultureInfo.InvariantCulture);
    }

    private static async Task<int> CountDuplicateOutboxIdempotencyKeysAsync(
        SqliteConnection connection,
        CancellationToken cancellationToken)
    {
        if (!await TableExistsAsync(connection, "NotificationOutbox", cancellationToken))
        {
            return 0;
        }

        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT COUNT(1)
            FROM (
                SELECT IdempotencyKey
                FROM NotificationOutbox
                WHERE IdempotencyKey IS NOT NULL
                  AND trim(IdempotencyKey) <> ''
                GROUP BY IdempotencyKey
                HAVING COUNT(1) > 1
            );
            """;
        return Convert.ToInt32(await command.ExecuteScalarAsync(cancellationToken), CultureInfo.InvariantCulture);
    }
}

public sealed class DatabaseVerificationReport
{
    public string Status { get; set; } = "FAIL";

    public string Mode { get; set; } = string.Empty;

    public string DataRoot { get; set; } = string.Empty;

    public string DatabasePath { get; set; } = string.Empty;

    public string SettingsPath { get; set; } = string.Empty;

    public bool SettingsFileExists { get; set; }

    public bool InitializedByCommand { get; set; }

    public bool DatabaseOpened { get; set; }

    public string IntegrityCheck { get; set; } = "not tested";

    public string ForeignKeyCheck { get; set; } = "not tested";

    public bool SchemaContractOk { get; set; }

    public string GeneratedAtUtc { get; set; } = string.Empty;

    public IReadOnlyList<string> ExpectedMigrations { get; set; } = [];

    public string[] MissingExpectedMigrations { get; set; } = [];

    public List<DatabaseMigrationSummary> Migrations { get; } = [];

    public Dictionary<string, DatabaseTableSummary> Tables { get; } = new(StringComparer.OrdinalIgnoreCase);

    public int OpenIncidentDuplicateCount { get; set; }

    public int OutboxIdempotencyDuplicateCount { get; set; }

    public List<string> Errors { get; } = [];
}

public sealed class DatabaseMigrationSummary
{
    public string Version { get; set; } = string.Empty;

    public string AppliedAtUtc { get; set; } = string.Empty;
}

public sealed class DatabaseTableSummary
{
    public bool Exists { get; set; }

    public long? Count { get; set; }
}
