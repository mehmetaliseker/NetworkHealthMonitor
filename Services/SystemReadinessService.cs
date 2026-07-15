using System.Diagnostics;
using System.Globalization;
using System.Reflection;
using Microsoft.Data.Sqlite;
using NetworkHealthMonitor.Data;
using NetworkHealthMonitor.Models;

namespace NetworkHealthMonitor.Services;

public sealed class SystemReadinessService
{
    private readonly SqliteConnectionFactory _connectionFactory;
    private readonly WorkerHeartbeatRepository _heartbeatRepository;
    private readonly INotificationOutboxRepository _outboxRepository;
    private readonly IWindowsServiceStatusService _serviceStatusService;

    public SystemReadinessService(
        SqliteConnectionFactory connectionFactory,
        WorkerHeartbeatRepository heartbeatRepository,
        INotificationOutboxRepository outboxRepository,
        IWindowsServiceStatusService serviceStatusService)
    {
        _connectionFactory = connectionFactory;
        _heartbeatRepository = heartbeatRepository;
        _outboxRepository = outboxRepository;
        _serviceStatusService = serviceStatusService;
    }

    public async Task<ServiceReadinessSnapshot> GetSnapshotAsync(AppSettings settings, CancellationToken cancellationToken = default)
    {
        var checks = new List<ReadinessCheckItem>();
        var diagnostics = new List<ReadinessCheckItem>();
        var nowUtc = DateTime.UtcNow;
        var service = await _serviceStatusService.GetStatusAsync(cancellationToken);
        var heartbeat = await _heartbeatRepository.GetLatestAsync(cancellationToken);
        var outboxCounts = await _outboxRepository.GetCountsAsync(cancellationToken);

        checks.Add(Check("Service kurulu", service.IsInstalled, service.DisplayText, service.Code));
        checks.Add(Check("Service Running", service.IsRunning, service.DisplayText, service.Code));
        checks.Add(Check("Startup type Automatic", service.IsAutomaticStartup, service.StartupType, "Automatic veya Automatic Delayed Start beklenir."));
        checks.Add(Check("Service recovery", service.RecoveryActionsConfigured, service.RecoveryActionsConfigured ? "Configured" : "Missing", "Failure recovery restart actions beklenir."));
        checks.Add(MapHeartbeatFreshness(heartbeat?.LastSeenAtUtc, nowUtc, settings.HeartbeatGraceSeconds, service.IsRunning));
        checks.Add(await CheckDatabaseAsync(cancellationToken));
        checks.Add(Check("Pending outbox", true, outboxCounts.Pending.ToString(CultureInfo.CurrentCulture), string.Empty));
        checks.Add(Check("Failed outbox", outboxCounts.Failed == 0, outboxCounts.Failed.ToString(CultureInfo.CurrentCulture), "Failed bildirimler retry edilmeli."));
        checks.Add(CheckDiskFree());
        checks.Add(Check("UI/Worker version", IsVersionMatch(GetUiVersion(), heartbeat?.Version), $"{GetUiVersion()} / {heartbeat?.Version ?? "-"}", "UI ve Worker publish ayni surumden gelmeli."));

        diagnostics.Add(Diagnostic("Process uptime", heartbeat is null ? "-" : AvailabilitySummaryReportItem.FormatDuration((long)(nowUtc - heartbeat.StartedAtUtc).TotalSeconds)));
        diagnostics.Add(Diagnostic("Worker restart count", (await GetWorkerRestartCountAsync(cancellationToken)).ToString(CultureInfo.CurrentCulture)));
        diagnostics.Add(Diagnostic("Son kritik hata", heartbeat?.LastCriticalError ?? string.Empty));
        diagnostics.Add(Diagnostic("Son database locked", heartbeat?.LastDatabaseLockedError ?? string.Empty));
        diagnostics.Add(Diagnostic("Son scheduler exception", heartbeat?.LastSchedulerException ?? string.Empty));
        diagnostics.Add(Diagnostic("Son ntfy exception", heartbeat?.LastNtfyException ?? string.Empty));
        diagnostics.Add(Diagnostic("Ortalama scheduler cycle", heartbeat is null ? "-" : $"{heartbeat.AverageSchedulerCycleMs:0} ms"));
        diagnostics.Add(Diagnostic("Son scheduler dongusu", FormatDate(heartbeat?.LastSchedulerCycleAtUtc)));
        diagnostics.Add(Diagnostic("Son scheduled ping", FormatDate(heartbeat?.LastSuccessfulPingAtUtc)));
        diagnostics.Add(Diagnostic("Son notification dispatch", FormatDate(heartbeat?.LastNotificationDispatchAtUtc)));
        var ping24 = await GetPingCounts24hAsync(cancellationToken);
        diagnostics.Add(Diagnostic("Son 24 saat toplam ping", ping24.Total.ToString(CultureInfo.CurrentCulture)));
        diagnostics.Add(Diagnostic("Son 24 saat basarisiz ping", ping24.Failed.ToString(CultureInfo.CurrentCulture)));
        diagnostics.Add(Diagnostic("Database file size", FormatBytes(GetFileSize(DatabasePaths.DatabaseFilePath))));
        diagnostics.Add(Diagnostic("Log directory size", FormatBytes(GetDirectorySize(DatabasePaths.LogDirectory))));
        diagnostics.Add(Diagnostic("Son backup", GetLastBackupText()));
        diagnostics.Add(Diagnostic("Uygulama surumu", GetUiVersion()));
        diagnostics.Add(Diagnostic("Worker surumu", heartbeat?.Version ?? "-"));

        return new ServiceReadinessSnapshot
        {
            Checks = checks,
            Diagnostics = diagnostics
        };
    }

    public static bool IsVersionMatch(string? uiVersion, string? workerVersion)
    {
        if (string.IsNullOrWhiteSpace(uiVersion) || string.IsNullOrWhiteSpace(workerVersion))
        {
            return false;
        }

        return string.Equals(NormalizeVersion(uiVersion), NormalizeVersion(workerVersion), StringComparison.OrdinalIgnoreCase);
    }

    public static ReadinessCheckItem MapHeartbeatFreshness(DateTime? lastSeenAtUtc, DateTime nowUtc, int graceSeconds, bool serviceRunning)
    {
        if (!lastSeenAtUtc.HasValue)
        {
            return new ReadinessCheckItem
            {
                Name = "Worker heartbeat guncel",
                Level = ReadinessLevel.Fail,
                Value = "-",
                Detail = "Heartbeat kaydi yok."
            };
        }

        var ageSeconds = Math.Max(0, (nowUtc.ToUniversalTime() - lastSeenAtUtc.Value.ToUniversalTime()).TotalSeconds);
        var allowed = Math.Max(30, graceSeconds);
        var healthy = ageSeconds <= allowed;
        return new ReadinessCheckItem
        {
            Name = "Worker heartbeat guncel",
            Level = healthy ? ReadinessLevel.Pass : ReadinessLevel.Fail,
            Value = $"{ageSeconds:0} sn",
            Detail = serviceRunning && !healthy
                ? "Service Running ancak heartbeat eski; sistem saglikli sayilmaz."
                : $"Grace {allowed} sn."
        };
    }

    private async Task<ReadinessCheckItem> CheckDatabaseAsync(CancellationToken cancellationToken)
    {
        try
        {
            await using var connection = await _connectionFactory.CreateOpenConnectionAsync();
            await using var command = connection.CreateCommand();
            command.CommandText = "SELECT 1;";
            await command.ExecuteScalarAsync(cancellationToken);
            return Check("Veritabani erisilebilir", true, "OK", DatabasePaths.DatabaseFilePath);
        }
        catch (Exception ex)
        {
            return new ReadinessCheckItem
            {
                Name = "Veritabani erisilebilir",
                Level = ReadinessLevel.Fail,
                Value = "FAIL",
                Detail = ex.Message
            };
        }
    }

    private static ReadinessCheckItem CheckDiskFree()
    {
        try
        {
            var root = Path.GetPathRoot(DatabasePaths.RootDirectory) ?? DatabasePaths.RootDirectory;
            var drive = new DriveInfo(root);
            var free = drive.AvailableFreeSpace;
            return Check("Disk bos alani", free > 1024L * 1024L * 1024L, FormatBytes(free), "En az 1 GB onerilir.");
        }
        catch (Exception ex)
        {
            return new ReadinessCheckItem
            {
                Name = "Disk bos alani",
                Level = ReadinessLevel.Warning,
                Value = "-",
                Detail = ex.Message
            };
        }
    }

    private async Task<int> GetWorkerRestartCountAsync(CancellationToken cancellationToken)
    {
        await using var connection = await _connectionFactory.CreateOpenConnectionAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT COUNT(1) FROM WorkerHeartbeat;";
        var result = await command.ExecuteScalarAsync(cancellationToken);
        return Convert.ToInt32(result, CultureInfo.InvariantCulture);
    }

    private async Task<(int Total, int Failed)> GetPingCounts24hAsync(CancellationToken cancellationToken)
    {
        await using var connection = await _connectionFactory.CreateOpenConnectionAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT COUNT(1),
                   SUM(CASE WHEN Status IN ('Offline','PingBlockedOrNoReply','Warning','UnderWatch') OR IsReachable = 0 THEN 1 ELSE 0 END)
            FROM PingLogs
            WHERE CheckedAt >= @SinceUtc;
            """;
        command.Parameters.AddWithValue("@SinceUtc", DateTime.UtcNow.AddHours(-24).ToString("O", CultureInfo.InvariantCulture));
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken))
        {
            return (0, 0);
        }

        return (
            Convert.ToInt32(reader.GetValue(0), CultureInfo.InvariantCulture),
            reader.IsDBNull(1) ? 0 : Convert.ToInt32(reader.GetValue(1), CultureInfo.InvariantCulture));
    }

    private static ReadinessCheckItem Check(string name, bool passed, string value, string detail)
    {
        return new ReadinessCheckItem
        {
            Name = name,
            Level = passed ? ReadinessLevel.Pass : ReadinessLevel.Fail,
            Value = value,
            Detail = detail
        };
    }

    private static ReadinessCheckItem Diagnostic(string name, string value)
    {
        return new ReadinessCheckItem
        {
            Name = name,
            Level = ReadinessLevel.Pass,
            Value = string.IsNullOrWhiteSpace(value) ? "-" : value,
            Detail = string.Empty
        };
    }

    private static string GetUiVersion()
    {
        return Assembly.GetEntryAssembly()?.GetName().Version?.ToString()
               ?? Assembly.GetExecutingAssembly().GetName().Version?.ToString()
               ?? "unknown";
    }

    private static string NormalizeVersion(string value)
    {
        return Version.TryParse(value, out var version)
            ? $"{version.Major}.{version.Minor}.{version.Build}"
            : value.Trim();
    }

    private static string FormatDate(DateTime? value)
    {
        return value.HasValue ? value.Value.ToLocalTime().ToString("dd.MM.yyyy HH:mm:ss", CultureInfo.CurrentCulture) : "-";
    }

    private static long GetFileSize(string path)
    {
        return File.Exists(path) ? new FileInfo(path).Length : 0;
    }

    private static long GetDirectorySize(string path)
    {
        if (!Directory.Exists(path))
        {
            return 0;
        }

        return Directory.EnumerateFiles(path, "*", SearchOption.AllDirectories).Sum(file => new FileInfo(file).Length);
    }

    private static string GetLastBackupText()
    {
        if (!Directory.Exists(DatabasePaths.BackupDirectory))
        {
            return "-";
        }

        var last = Directory.EnumerateDirectories(DatabasePaths.BackupDirectory)
            .Select(path => new DirectoryInfo(path))
            .OrderByDescending(info => info.LastWriteTimeUtc)
            .FirstOrDefault();
        return last is null ? "-" : last.LastWriteTime.ToString("dd.MM.yyyy HH:mm:ss", CultureInfo.CurrentCulture);
    }

    private static string FormatBytes(long bytes)
    {
        string[] units = ["B", "KB", "MB", "GB", "TB"];
        double value = bytes;
        var unit = 0;
        while (value >= 1024 && unit < units.Length - 1)
        {
            value /= 1024;
            unit++;
        }

        return $"{value:0.##} {units[unit]}";
    }
}
