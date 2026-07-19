using System.Diagnostics;
using System.Globalization;
using System.Reflection;
using Microsoft.Data.Sqlite;
using NetworkHealthMonitor.Data;
using NetworkHealthMonitor.Infrastructure;
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

        checks.Add(Check("Servis kurulu", service.IsInstalled, service.DisplayText, service.Code));
        checks.Add(Check("İzleme servisi çalışıyor", service.IsRunning, service.DisplayText, service.Code));
        checks.Add(Check("Otomatik başlatma", service.IsAutomaticStartup, service.StartupType, "Automatic veya Automatic Delayed Start beklenir."));
        checks.Add(Check("Servis kurtarma", service.RecoveryActionsConfigured, service.RecoveryActionsConfigured ? "Yapılandırıldı" : "Eksik", "Başarısızlık sonrası yeniden başlatma eylemleri beklenir."));
        checks.Add(MapHeartbeatFreshness(heartbeat?.LastSeenAtUtc, nowUtc, settings.HeartbeatGraceSeconds, service.IsRunning));
        checks.Add(await CheckDatabaseAsync(cancellationToken));
        checks.Add(Check("Bekleyen bildirimler", true, outboxCounts.Pending.ToString(CultureInfo.CurrentCulture), string.Empty));
        checks.Add(Check("Başarısız bildirimler", outboxCounts.Failed == 0, outboxCounts.Failed.ToString(CultureInfo.CurrentCulture), "Başarısız bildirimler yeniden denenmelidir."));
        checks.Add(CheckDiskFree());
        checks.Add(Check("UI / izleme servisi sürümü", IsVersionMatch(GetUiVersion(), heartbeat?.Version), $"{GetUiVersion()} / {heartbeat?.Version ?? "-"}", "UI ve izleme servisi aynı yayından gelmelidir."));

        diagnostics.Add(Diagnostic("Süreç çalışma süresi", heartbeat is null ? "-" : AvailabilitySummaryReportItem.FormatDuration((long)(nowUtc - heartbeat.StartedAtUtc).TotalSeconds)));
        diagnostics.Add(Diagnostic("İzleme servisi yeniden başlatma sayısı", (await GetWorkerRestartCountAsync(cancellationToken)).ToString(CultureInfo.CurrentCulture)));
        diagnostics.Add(Diagnostic("Son kritik hata", heartbeat?.LastCriticalError ?? string.Empty));
        diagnostics.Add(Diagnostic("Son veritabanı kilit hatası", heartbeat?.LastDatabaseLockedError ?? string.Empty));
        diagnostics.Add(Diagnostic("Son zamanlayıcı hatası", heartbeat?.LastSchedulerException ?? string.Empty));
        diagnostics.Add(Diagnostic("Son ntfy hatası", heartbeat?.LastNtfyException ?? string.Empty));
        diagnostics.Add(Diagnostic("Ortalama zamanlayıcı döngüsü", heartbeat is null ? "-" : $"{heartbeat.AverageSchedulerCycleMs:0} ms"));
        diagnostics.Add(Diagnostic("Son zamanlayıcı yoklaması", FormatDate(heartbeat?.LastSchedulerPollAtUtc)));
        diagnostics.Add(Diagnostic("Son zamanlayıcı döngüsü", FormatDate(heartbeat?.LastSchedulerCycleAtUtc)));
        diagnostics.Add(Diagnostic("Son otomatik kontrol", FormatDate(heartbeat?.LastSuccessfulPingAtUtc)));
        diagnostics.Add(Diagnostic("Son bildirim gönderimi", FormatDate(heartbeat?.LastNotificationDispatchAtUtc)));
        var ping24 = await GetPingCounts24hAsync(cancellationToken);
        diagnostics.Add(Diagnostic("Son 24 saat toplam ping", ping24.Total.ToString(CultureInfo.CurrentCulture)));
        diagnostics.Add(Diagnostic("Son 24 saat başarısız ping", ping24.Failed.ToString(CultureInfo.CurrentCulture)));
        diagnostics.Add(Diagnostic("Veritabanı dosya boyutu", FormatBytes(GetFileSize(DatabasePaths.DatabaseFilePath))));
        diagnostics.Add(Diagnostic("Log klasörü boyutu", FormatBytes(GetDirectorySize(DatabasePaths.LogDirectory))));
        diagnostics.Add(Diagnostic("Son yedek", GetLastBackupText()));
        var build = ApplicationBuildInfo.Current;
        diagnostics.Add(Diagnostic("Ürün sürümü", build.ProductVersion));
        diagnostics.Add(Diagnostic("Dosya sürümü", build.FileVersion));
        diagnostics.Add(Diagnostic("Derleme zamanı (UTC)", build.BuildTimestampUtc));
        diagnostics.Add(Diagnostic("Git commit SHA", build.GitCommitSha));
        diagnostics.Add(Diagnostic("Beklenen şema sürümü", build.ExpectedSchemaVersion));
        diagnostics.Add(Diagnostic("Aktif veritabanı yolu", DatabasePaths.DatabaseFilePath));
        diagnostics.Add(Diagnostic("Uygulama sürümü", GetUiVersion()));
        diagnostics.Add(Diagnostic("İzleme servisi sürümü", heartbeat?.Version ?? "-"));

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
                Name = "İzleme servisi heartbeat güncel",
                Level = ReadinessLevel.Fail,
                Value = "-",
                Detail = "Heartbeat kaydı yok."
            };
        }

        var ageSeconds = Math.Max(0, (nowUtc.ToUniversalTime() - lastSeenAtUtc.Value.ToUniversalTime()).TotalSeconds);
        var allowed = Math.Max(30, graceSeconds);
        var healthy = ageSeconds <= allowed;
        return new ReadinessCheckItem
        {
            Name = "İzleme servisi heartbeat güncel",
            Level = healthy ? ReadinessLevel.Pass : ReadinessLevel.Fail,
            Value = $"{ageSeconds:0} sn",
            Detail = serviceRunning && !healthy
                ? "İzleme servisi çalışıyor ancak heartbeat eski; sistem sağlıklı sayılmaz."
                : $"Tolerans {allowed} sn."
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
            return Check("Veritabanı erişilebilir", true, "Tamam", DatabasePaths.DatabaseFilePath);
        }
        catch (Exception ex)
        {
            return new ReadinessCheckItem
            {
                Name = "Veritabanı erişilebilir",
                Level = ReadinessLevel.Fail,
                Value = "Başarısız",
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
            return Check("Disk boş alanı", free > 1024L * 1024L * 1024L, FormatBytes(free), "En az 1 GB önerilir.");
        }
        catch (Exception ex)
        {
            return new ReadinessCheckItem
            {
                Name = "Disk boş alanı",
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
