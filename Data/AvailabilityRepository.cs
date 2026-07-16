using System.Globalization;
using Microsoft.Data.Sqlite;
using NetworkHealthMonitor.Models;

namespace NetworkHealthMonitor.Data;

public sealed class AvailabilityRepository
{
    public const int CalculationVersion = 1;
    private readonly SqliteConnectionFactory _connectionFactory;

    public AvailabilityRepository(SqliteConnectionFactory connectionFactory)
    {
        _connectionFactory = connectionFactory;
    }

    public async Task ApplyPingResultsAsync(
        IReadOnlyList<PingDeviceResult> results,
        IReadOnlyDictionary<int, PingLog> logsByDeviceId,
        DowntimeStartPolicy downtimeStartPolicy,
        CancellationToken cancellationToken = default)
    {
        if (results.Count == 0)
        {
            return;
        }

        await using var connection = await _connectionFactory.CreateOpenConnectionAsync();
        using var transaction = connection.BeginTransaction();

        foreach (var result in results.OrderBy(item => item.CheckedAt))
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!logsByDeviceId.TryGetValue(result.Device.Id, out var log))
            {
                continue;
            }

            var checkedAtUtc = log.CheckedAt.ToUniversalTime();
            if (await IsInMaintenanceAsync(connection, transaction, result.Device.Id, result.Device.GroupId, checkedAtUtc, cancellationToken))
            {
                await TransitionAsync(
                    connection,
                    transaction,
                    result.Device.Id,
                    AvailabilityStatus.Maintenance,
                    checkedAtUtc,
                    incidentId: null,
                    "MaintenanceWindow",
                    "Planli bakim penceresi aktif.",
                    "WorkerPing",
                    firstFailureAtUtc: null,
                    confirmedAtUtc: null,
                    cancellationToken);
                continue;
            }

            if (result.IsSuccess)
            {
                await TransitionAsync(
                    connection,
                    transaction,
                    result.Device.Id,
                    AvailabilityStatus.Up,
                    checkedAtUtc,
                    incidentId: null,
                    "PingSuccess",
                    "Ping basarili.",
                    "WorkerPing",
                    firstFailureAtUtc: null,
                    confirmedAtUtc: null,
                    cancellationToken);
                continue;
            }

            if (result.Status == DeviceStatus.Offline)
            {
                var firstFailureAtUtc = await GetOpenFirstFailureAsync(connection, transaction, result.Device.Id, cancellationToken)
                    ?? checkedAtUtc;
                var downStartUtc = downtimeStartPolicy == DowntimeStartPolicy.FirstFailedCheck
                    ? firstFailureAtUtc
                    : checkedAtUtc;
                var incidentId = await GetOpenIncidentIdAsync(connection, transaction, result.Device.Id, cancellationToken);

                await TransitionAsync(
                    connection,
                    transaction,
                    result.Device.Id,
                    AvailabilityStatus.Down,
                    downStartUtc,
                    incidentId,
                    "ConfirmedDown",
                    "Failure threshold reached.",
                    "WorkerPing",
                    firstFailureAtUtc,
                    checkedAtUtc,
                    cancellationToken);
                continue;
            }

            await MarkSuspectedFailureAsync(connection, transaction, result.Device.Id, checkedAtUtc, cancellationToken);
        }

        transaction.Commit();
    }

    public async Task TransitionDeviceToPausedAsync(
        int deviceId,
        DateTime startedAtUtc,
        string reasonCode,
        string reasonText,
        CancellationToken cancellationToken = default)
    {
        await using var connection = await _connectionFactory.CreateOpenConnectionAsync();
        using var transaction = connection.BeginTransaction();
        await TransitionAsync(
            connection,
            transaction,
            deviceId,
            AvailabilityStatus.Paused,
            startedAtUtc.ToUniversalTime(),
            incidentId: null,
            reasonCode,
            reasonText,
            "Management",
            firstFailureAtUtc: null,
            confirmedAtUtc: null,
            cancellationToken);
        transaction.Commit();
    }

    public async Task ReconcileWorkerHeartbeatGapAsync(
        DateTime? previousLastSeenAtUtc,
        DateTime workerStartedAtUtc,
        int heartbeatGraceSeconds,
        CancellationToken cancellationToken = default)
    {
        if (!previousLastSeenAtUtc.HasValue)
        {
            return;
        }

        var previousUtc = previousLastSeenAtUtc.Value.ToUniversalTime();
        var startedUtc = workerStartedAtUtc.ToUniversalTime();
        var gapStartUtc = previousUtc.AddSeconds(Math.Max(1, heartbeatGraceSeconds));
        if (startedUtc <= gapStartUtc)
        {
            return;
        }

        await using var connection = await _connectionFactory.CreateOpenConnectionAsync();
        using var transaction = connection.BeginTransaction();
        var deviceIds = await GetActiveDeviceIdsAsync(connection, transaction, cancellationToken);
        foreach (var deviceId in deviceIds)
        {
            cancellationToken.ThrowIfCancellationRequested();
            await TransitionAsync(
                connection,
                transaction,
                deviceId,
                AvailabilityStatus.Unknown,
                gapStartUtc,
                incidentId: null,
                "WorkerHeartbeatGap",
                "Worker heartbeat grace suresi asildi; izleme verisi yok.",
                "WorkerHeartbeat",
                firstFailureAtUtc: null,
                confirmedAtUtc: null,
                cancellationToken);
        }

        transaction.Commit();
    }

    public async Task<int> ReconcileExpectedCheckGapsAsync(
        int expectedCheckGraceMultiplier,
        int defaultCheckIntervalSeconds,
        DateTime nowUtc,
        CancellationToken cancellationToken = default)
    {
        var affected = 0;
        await using var connection = await _connectionFactory.CreateOpenConnectionAsync();
        using var transaction = connection.BeginTransaction();

        await using var select = connection.CreateCommand();
        select.Transaction = transaction;
        select.CommandText = """
            SELECT Id, COALESCE(LastCheckedAt, ''), CheckIntervalSeconds
            FROM Devices
            WHERE IsDeleted = 0
              AND IsActive = 1
              AND IsEnabled = 1
              AND AutoCheckEnabled = 1;
            """;

        var rows = new List<(int DeviceId, DateTime? LastCheckedAtUtc, int CheckIntervalSeconds)>();
        await using (var reader = await select.ExecuteReaderAsync(cancellationToken))
        {
            while (await reader.ReadAsync(cancellationToken))
            {
                rows.Add((
                    reader.GetInt32(0),
                    string.IsNullOrWhiteSpace(reader.GetString(1)) ? null : FromStorageDate(reader.GetString(1)),
                    reader.GetInt32(2)));
            }
        }

        foreach (var row in rows)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!row.LastCheckedAtUtc.HasValue)
            {
                continue;
            }

            var expectedSeconds = row.CheckIntervalSeconds > 0
                ? row.CheckIntervalSeconds
                : Math.Max(AppSettings.MinDeviceCheckIntervalSeconds, defaultCheckIntervalSeconds);
            var unknownAtUtc = row.LastCheckedAtUtc.Value.AddSeconds(expectedSeconds * Math.Max(1, expectedCheckGraceMultiplier));
            if (nowUtc.ToUniversalTime() <= unknownAtUtc)
            {
                continue;
            }

            var current = await GetOpenPeriodAsync(connection, transaction, row.DeviceId, cancellationToken);
            if (current?.Status is AvailabilityStatus.Unknown or AvailabilityStatus.Maintenance or AvailabilityStatus.Paused)
            {
                continue;
            }

            await TransitionAsync(
                connection,
                transaction,
                row.DeviceId,
                AvailabilityStatus.Unknown,
                unknownAtUtc,
                incidentId: null,
                "ExpectedCheckGap",
                "Beklenen ping kontrol araligi asildi.",
                "Scheduler",
                firstFailureAtUtc: null,
                confirmedAtUtc: null,
                cancellationToken);
            affected++;
        }

        transaction.Commit();
        return affected;
    }

    public async Task<int> ReconcileMaintenanceWindowsAsync(
        DateTime nowUtc,
        CancellationToken cancellationToken = default)
    {
        var affected = 0;
        var now = nowUtc.ToUniversalTime();
        await using var connection = await _connectionFactory.CreateOpenConnectionAsync();
        using var transaction = connection.BeginTransaction();

        var activeTargets = new List<(DateTime StartedAtUtc, MonitoringTargetType TargetType, int? TargetId)>();
        await using (var select = connection.CreateCommand())
        {
            select.Transaction = transaction;
            select.CommandText = """
                SELECT w.StartedAtUtc, t.TargetType, t.TargetId
                FROM MaintenanceWindows w
                INNER JOIN MaintenanceWindowTargets t ON t.MaintenanceWindowId = w.Id
                WHERE w.Status IN ('Scheduled','Active')
                  AND w.StartedAtUtc <= @NowUtc
                  AND w.EndedAtUtc > @NowUtc;
                """;
            AddParameter(select, "@NowUtc", ToStorageDate(now));
            await using var reader = await select.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
            {
                activeTargets.Add((
                    FromStorageDate(reader.GetString(0)),
                    ParseTargetType(reader.GetString(1)),
                    reader.IsDBNull(2) ? null : reader.GetInt32(2)));
            }
        }

        foreach (var target in activeTargets)
        {
            var deviceIds = await ResolveTargetDeviceIdsAsync(connection, transaction, target.TargetType, target.TargetId, cancellationToken);
            foreach (var deviceId in deviceIds)
            {
                cancellationToken.ThrowIfCancellationRequested();
                await TransitionAsync(
                    connection,
                    transaction,
                    deviceId,
                    AvailabilityStatus.Maintenance,
                    target.StartedAtUtc,
                    incidentId: null,
                    "MaintenanceWindow",
                    "Planli bakim penceresi aktif.",
                    "Maintenance",
                    firstFailureAtUtc: null,
                    confirmedAtUtc: null,
                    cancellationToken);
                affected++;
            }
        }

        var openMaintenance = new List<(int DeviceId, int? GroupId)>();
        await using (var selectOpen = connection.CreateCommand())
        {
            selectOpen.Transaction = transaction;
            selectOpen.CommandText = """
                SELECT p.DeviceId, d.GroupId
                FROM DeviceAvailabilityPeriods p
                INNER JOIN Devices d ON d.Id = p.DeviceId
                WHERE p.Status = 'Maintenance'
                  AND p.EndedAtUtc IS NULL;
                """;
            await using var reader = await selectOpen.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
            {
                openMaintenance.Add((reader.GetInt32(0), reader.IsDBNull(1) ? null : reader.GetInt32(1)));
            }
        }

        foreach (var item in openMaintenance)
        {
            if (await IsInMaintenanceAsync(connection, transaction, item.DeviceId, item.GroupId, now, cancellationToken))
            {
                continue;
            }

            await TransitionAsync(
                connection,
                transaction,
                item.DeviceId,
                AvailabilityStatus.Unknown,
                now,
                incidentId: null,
                "MaintenanceEnded",
                "Bakim penceresi bitti; ilk dogrulamaya kadar Unknown.",
                "Maintenance",
                firstFailureAtUtc: null,
                confirmedAtUtc: null,
                cancellationToken);
            affected++;
        }

        transaction.Commit();
        return affected;
    }

    public async Task<IReadOnlyList<DeviceAvailabilityPeriod>> GetPeriodsAsync(
        int? deviceId,
        DateTime startUtc,
        DateTime endUtc,
        CancellationToken cancellationToken = default)
    {
        var periods = new List<DeviceAvailabilityPeriod>();
        await using var connection = await _connectionFactory.CreateOpenConnectionAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT Id, DeviceId, Status, StartedAtUtc, EndedAtUtc, DurationSeconds, IncidentId,
                   ReasonCode, ReasonText, DetectionSource, FirstFailureAtUtc, ConfirmedAtUtc,
                   CreatedAtUtc, UpdatedAtUtc
            FROM DeviceAvailabilityPeriods
            WHERE (@DeviceId IS NULL OR DeviceId = @DeviceId)
              AND StartedAtUtc < @EndUtc
              AND (EndedAtUtc IS NULL OR EndedAtUtc > @StartUtc)
            ORDER BY DeviceId, StartedAtUtc;
            """;
        AddParameter(command, "@DeviceId", deviceId);
        AddParameter(command, "@StartUtc", ToStorageDate(startUtc));
        AddParameter(command, "@EndUtc", ToStorageDate(endUtc));

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            periods.Add(ReadPeriod(reader));
        }

        return periods;
    }

    public async Task<IReadOnlyList<AvailabilitySummaryReportItem>> GetSummaryAsync(
        DateTime startUtc,
        DateTime endUtc,
        string timezoneId,
        int? groupId = null,
        bool includeDeleted = false,
        CancellationToken cancellationToken = default)
    {
        await using var connection = await _connectionFactory.CreateOpenConnectionAsync();
        var devices = await GetReportDevicesAsync(connection, groupId, includeDeleted, cancellationToken);
        var periods = await GetPeriodsAsync(null, startUtc, endUtc, cancellationToken);
        var periodsByDevice = periods.GroupBy(period => period.DeviceId).ToDictionary(group => group.Key, group => group.ToList());
        var incidentsByDevice = await GetIncidentStatsAsync(connection, startUtc, endUtc, cancellationToken);
        var rows = new List<AvailabilitySummaryReportItem>();
        var nowUtc = DateTime.UtcNow;

        foreach (var device in devices)
        {
            periodsByDevice.TryGetValue(device.Id, out var devicePeriods);
            var totals = CalculateTotals(devicePeriods ?? new List<DeviceAvailabilityPeriod>(), startUtc, endUtc);
            incidentsByDevice.TryGetValue(device.Id, out var incidentStats);
            var current = devicePeriods?
                .Where(period => !period.EndedAtUtc.HasValue)
                .OrderByDescending(period => period.StartedAtUtc)
                .FirstOrDefault();
            var currentStatus = current?.Status ?? AvailabilityStatus.Unknown;
            var continuousUpSeconds = currentStatus == AvailabilityStatus.Up && current is not null
                ? Math.Max(0, (long)(nowUtc - current.StartedAtUtc).TotalSeconds)
                : 0;
            var slaTarget = device.SlaTargetAvailabilityPercent;
            var slaStatus = CalculateSlaStatus(slaTarget, totals.AvailabilityPercent);

            rows.Add(new AvailabilitySummaryReportItem
            {
                DeviceId = device.Id,
                DeviceName = device.Name,
                IpAddress = device.IpAddress,
                DeviceType = device.DeviceType,
                GroupName = device.GroupName,
                ReportStartUtc = startUtc,
                ReportEndUtc = endUtc,
                TimezoneId = timezoneId,
                ExpectedMonitoringSeconds = Math.Max(0, (long)(endUtc - startUtc).TotalSeconds),
                UpSeconds = totals.UpSeconds,
                DownSeconds = totals.DownSeconds,
                UnknownSeconds = totals.UnknownSeconds,
                MaintenanceSeconds = totals.MaintenanceSeconds,
                PausedSeconds = totals.PausedSeconds,
                IncidentCount = incidentStats.IncidentCount,
                RecoveredIncidentCount = incidentStats.RecoveredIncidentCount,
                MttrSeconds = incidentStats.MttrSeconds,
                MtbfSeconds = incidentStats.IncidentCount == 0 ? totals.UpSeconds : totals.UpSeconds / incidentStats.IncidentCount,
                LongestOutageSeconds = Math.Max(totals.LongestOutageSeconds, incidentStats.LongestOutageSeconds),
                TotalDetectionDelaySeconds = totals.TotalDetectionDelaySeconds,
                AvailabilityPercent = totals.AvailabilityPercent,
                StrictAvailabilityPercent = totals.StrictAvailabilityPercent,
                CoveragePercent = totals.CoveragePercent,
                CurrentStatus = currentStatus,
                CurrentStatusSinceUtc = current?.StartedAtUtc,
                CurrentContinuousAvailabilitySeconds = continuousUpSeconds,
                LastCheckedAtUtc = device.LastCheckedAt?.ToUniversalTime(),
                LastSuccessfulCheckAtUtc = device.LastSuccessfulCheckAt?.ToUniversalTime(),
                SlaTargetPercent = slaTarget,
                SlaStatus = slaStatus
            });
        }

        return rows.OrderBy(row => row.DeviceName, StringComparer.CurrentCultureIgnoreCase).ToList();
    }

    public async Task<AvailabilityDashboardSummary> GetDashboardSummaryAsync(
        DateTime nowUtc,
        CancellationToken cancellationToken = default)
    {
        var summary30 = await GetSummaryAsync(nowUtc.AddDays(-30), nowUtc, TimeZoneInfo.Local.Id, cancellationToken: cancellationToken);
        var summary7 = await GetSummaryAsync(nowUtc.AddDays(-7), nowUtc, TimeZoneInfo.Local.Id, cancellationToken: cancellationToken);
        var summary24 = await GetSummaryAsync(nowUtc.AddHours(-24), nowUtc, TimeZoneInfo.Local.Id, cancellationToken: cancellationToken);
        var current = summary30.GroupBy(item => item.CurrentStatus).ToDictionary(group => group.Key, group => group.Count());

        return new AvailabilityDashboardSummary
        {
            TotalActiveDevices = summary30.Count,
            Up = current.GetValueOrDefault(AvailabilityStatus.Up),
            Down = current.GetValueOrDefault(AvailabilityStatus.Down),
            Unknown = current.GetValueOrDefault(AvailabilityStatus.Unknown),
            Maintenance = current.GetValueOrDefault(AvailabilityStatus.Maintenance),
            OpenIncidentCount = summary30.Count(item => item.CurrentStatus == AvailabilityStatus.Down),
            Availability24HoursPercent = WeightedAvailability(summary24),
            Availability7DaysPercent = WeightedAvailability(summary7),
            Availability30DaysPercent = WeightedAvailability(summary30),
            CoveragePercent = WeightedCoverage(summary30),
            SlaViolationDeviceCount = summary30.Count(item => string.Equals(item.SlaStatus, "Ihlal", StringComparison.OrdinalIgnoreCase))
        };
    }

    public async Task<IReadOnlyList<AvailabilityTrendPoint>> GetDailyTrendAsync(
        DateTime startUtc,
        DateTime endUtc,
        string timezoneId,
        CancellationToken cancellationToken = default)
    {
        var timezone = ResolveTimezone(timezoneId);
        var startDate = DateOnly.FromDateTime(TimeZoneInfo.ConvertTimeFromUtc(startUtc.ToUniversalTime(), timezone));
        var endDate = DateOnly.FromDateTime(TimeZoneInfo.ConvertTimeFromUtc(endUtc.ToUniversalTime(), timezone));
        var rows = new List<AvailabilityTrendPoint>();
        for (var date = startDate; date <= endDate; date = date.AddDays(1))
        {
            var localStart = date.ToDateTime(TimeOnly.MinValue, DateTimeKind.Unspecified);
            var localEnd = date.AddDays(1).ToDateTime(TimeOnly.MinValue, DateTimeKind.Unspecified);
            var dayStartUtc = Max(TimeZoneInfo.ConvertTimeToUtc(localStart, timezone), startUtc.ToUniversalTime());
            var dayEndUtc = Min(TimeZoneInfo.ConvertTimeToUtc(localEnd, timezone), endUtc.ToUniversalTime());
            if (dayEndUtc <= dayStartUtc)
            {
                continue;
            }

            var summary = await GetSummaryAsync(dayStartUtc, dayEndUtc, timezoneId, cancellationToken: cancellationToken);
            var up = summary.Sum(item => item.UpSeconds);
            var down = summary.Sum(item => item.DownSeconds);
            var unknown = summary.Sum(item => item.UnknownSeconds);
            var maintenance = summary.Sum(item => item.MaintenanceSeconds);
            var expected = summary.Sum(item => item.ExpectedMonitoringSeconds);
            var known = up + down;
            rows.Add(new AvailabilityTrendPoint
            {
                Date = date,
                UpSeconds = up,
                DownSeconds = down,
                UnknownSeconds = unknown,
                MaintenanceSeconds = maintenance,
                ExpectedMonitoringSeconds = expected,
                AvailabilityPercent = known > 0 ? up * 100d / known : null,
                CoveragePercent = expected > 0 ? known * 100d / expected : null
            });
        }

        return rows;
    }

    public async Task<IReadOnlyList<AvailabilityRankingRow>> GetLongestOutagesAsync(
        DateTime startUtc,
        DateTime endUtc,
        int limit,
        CancellationToken cancellationToken = default)
    {
        var rows = new List<AvailabilityRankingRow>();
        await using var connection = await _connectionFactory.CreateOpenConnectionAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT d.Name, d.IpAddress, COALESCE(d.GroupName, ''), p.StartedAtUtc,
                   COALESCE(p.EndedAtUtc, @EndUtc) AS EffectiveEndUtc
            FROM DeviceAvailabilityPeriods p
            INNER JOIN Devices d ON d.Id = p.DeviceId
            WHERE p.Status = 'Down'
              AND p.StartedAtUtc < @EndUtc
              AND (p.EndedAtUtc IS NULL OR p.EndedAtUtc > @StartUtc)
            ORDER BY (julianday(COALESCE(p.EndedAtUtc, @EndUtc)) - julianday(p.StartedAtUtc)) DESC
            LIMIT @Limit;
            """;
        AddParameter(command, "@StartUtc", ToStorageDate(startUtc));
        AddParameter(command, "@EndUtc", ToStorageDate(endUtc));
        AddParameter(command, "@Limit", Math.Clamp(limit, 1, 100));
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            var started = FromStorageDate(reader.GetString(3));
            var ended = FromStorageDate(reader.GetString(4));
            var seconds = Math.Max(0, (long)(Min(ended, endUtc) - Max(started, startUtc)).TotalSeconds);
            rows.Add(new AvailabilityRankingRow
            {
                Title = reader.GetString(0),
                Subtitle = $"{reader.GetString(1)} {reader.GetString(2)}".Trim(),
                Value = AvailabilitySummaryReportItem.FormatDuration(seconds),
                Percent = Math.Min(100, seconds / 864d)
            });
        }

        return rows;
    }

    public async Task<IReadOnlyList<AvailabilityRankingRow>> GetIncidentRankingAsync(
        DateTime startUtc,
        DateTime endUtc,
        int limit,
        CancellationToken cancellationToken = default)
    {
        var rows = new List<AvailabilityRankingRow>();
        await using var connection = await _connectionFactory.CreateOpenConnectionAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT d.Name, d.IpAddress, COALESCE(d.GroupName, ''), COUNT(i.Id) AS IncidentCount
            FROM DeviceIncidents i
            INNER JOIN Devices d ON d.Id = i.DeviceId
            WHERE i.StartedAtUtc < @EndUtc
              AND (i.EndedAtUtc IS NULL OR i.EndedAtUtc > @StartUtc)
            GROUP BY d.Id, d.Name, d.IpAddress, d.GroupName
            ORDER BY IncidentCount DESC, d.Name COLLATE NOCASE
            LIMIT @Limit;
            """;
        AddParameter(command, "@StartUtc", ToStorageDate(startUtc));
        AddParameter(command, "@EndUtc", ToStorageDate(endUtc));
        AddParameter(command, "@Limit", Math.Clamp(limit, 1, 100));
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            var count = reader.GetInt32(3);
            rows.Add(new AvailabilityRankingRow
            {
                Title = reader.GetString(0),
                Subtitle = $"{reader.GetString(1)} {reader.GetString(2)}".Trim(),
                Value = count.ToString(CultureInfo.CurrentCulture),
                Percent = Math.Min(100, count * 10d)
            });
        }

        return rows;
    }

    public async Task RecalculateDailyAsync(
        DateOnly startDate,
        DateOnly endDate,
        string timezoneId,
        int? deviceId = null,
        int? groupId = null,
        CancellationToken cancellationToken = default)
    {
        if (endDate < startDate)
        {
            throw new ArgumentException("End date must be greater than or equal to start date.", nameof(endDate));
        }

        var timezone = ResolveTimezone(timezoneId);
        await using var connection = await _connectionFactory.CreateOpenConnectionAsync();
        using var transaction = connection.BeginTransaction();
        var devices = await GetReportDevicesAsync(connection, groupId, includeDeleted: true, cancellationToken);
        if (deviceId.HasValue)
        {
            devices = devices.Where(device => device.Id == deviceId.Value).ToList();
        }

        for (var date = startDate; date <= endDate; date = date.AddDays(1))
        {
            var localStart = date.ToDateTime(TimeOnly.MinValue, DateTimeKind.Unspecified);
            var localEnd = date.AddDays(1).ToDateTime(TimeOnly.MinValue, DateTimeKind.Unspecified);
            var startUtc = TimeZoneInfo.ConvertTimeToUtc(localStart, timezone);
            var endUtc = TimeZoneInfo.ConvertTimeToUtc(localEnd, timezone);
            var incidentStats = await GetIncidentStatsAsync(connection, startUtc, endUtc, cancellationToken);

            foreach (var device in devices)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var monitoringIntervals = await GetMonitoringIntervalsAsync(
                    connection,
                    transaction,
                    device,
                    date,
                    timezone,
                    startUtc,
                    endUtc,
                    cancellationToken);
                var periods = await GetPeriodsForDeviceAsync(connection, transaction, device.Id, startUtc, endUtc, cancellationToken);
                var expectedSeconds = monitoringIntervals.Sum(interval => Math.Max(0, (long)(interval.EndUtc - interval.StartUtc).TotalSeconds));
                var totals = SumTotals(monitoringIntervals.Select(interval => CalculateTotals(periods, interval.StartUtc, interval.EndUtc)));
                incidentStats.TryGetValue(device.Id, out var stats);
                await UpsertDailyAsync(connection, transaction, device.Id, date, timezone.Id, expectedSeconds, totals, stats, cancellationToken);
            }
        }

        await InsertAuditAsync(connection, transaction, "AvailabilityRecalculation", deviceId, groupId, startDate, endDate, "Succeeded", string.Empty, cancellationToken);
        transaction.Commit();
    }

    public async Task<IReadOnlyList<AvailabilityIncidentReportItem>> GetIncidentReportAsync(
        DateTime startUtc,
        DateTime endUtc,
        int? deviceId = null,
        CancellationToken cancellationToken = default)
    {
        var rows = new List<AvailabilityIncidentReportItem>();
        await using var connection = await _connectionFactory.CreateOpenConnectionAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT i.Id, i.DeviceId, d.Name, d.IpAddress, d.GroupName,
                   COALESCE(i.FirstFailureAtUtc, i.StartedAtUtc) AS FirstFailureAtUtc,
                   COALESCE(i.ConfirmedDownAtUtc, i.LastFailureAtUtc, i.StartedAtUtc) AS ConfirmedAtUtc,
                   i.EndedAtUtc, i.CurrentFailureCount,
                   COALESCE(l.ErrorCode, ''), COALESCE(l.ErrorMessage, ''),
                   COALESCE(n.Status, ''),
                   CASE WHEN EXISTS (
                       SELECT 1
                       FROM DeviceAvailabilityPeriods p
                       WHERE p.DeviceId = i.DeviceId
                         AND p.Status = 'Maintenance'
                          AND p.StartedAtUtc < COALESCE(i.EndedAtUtc, @EndUtc)
                         AND COALESCE(p.EndedAtUtc, @EndUtc) > i.StartedAtUtc
                   ) THEN 1 ELSE 0 END AS MaintenanceRelated
            FROM DeviceIncidents i
            INNER JOIN Devices d ON d.Id = i.DeviceId
            LEFT JOIN PingLogs l ON l.DeviceId = i.DeviceId AND l.CheckedAt = i.StartedAtUtc
            LEFT JOIN NotificationOutbox n ON n.IncidentId = i.Id AND n.EventType = 'DeviceDown'
            WHERE i.StartedAtUtc < @EndUtc
              AND COALESCE(i.EndedAtUtc, @EndUtc) > @StartUtc
              AND (@DeviceId IS NULL OR i.DeviceId = @DeviceId)
            ORDER BY i.StartedAtUtc DESC;
            """;
        AddParameter(command, "@StartUtc", ToStorageDate(startUtc));
        AddParameter(command, "@EndUtc", ToStorageDate(endUtc));
        AddParameter(command, "@DeviceId", deviceId);

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            var firstFailure = FromStorageDate(reader.GetString(5));
            var confirmed = FromStorageDate(reader.GetString(6));
            var recovered = reader.IsDBNull(7) ? null : (DateTime?)FromStorageDate(reader.GetString(7));
            var effectiveEnd = recovered ?? endUtc;
            rows.Add(new AvailabilityIncidentReportItem
            {
                IncidentId = reader.GetInt64(0),
                DeviceId = reader.GetInt32(1),
                DeviceName = reader.GetString(2),
                IpAddress = reader.GetString(3),
                GroupName = reader.GetString(4),
                FirstFailureAtUtc = firstFailure,
                ConfirmedDownAtUtc = confirmed,
                EndedAtUtc = recovered,
                DowntimeSeconds = Math.Max(0, (long)(effectiveEnd - firstFailure).TotalSeconds),
                DetectionDelaySeconds = Math.Max(0, (long)(confirmed - firstFailure).TotalSeconds),
                FailureCount = reader.GetInt32(8),
                ErrorCode = reader.GetString(9),
                ErrorMessage = reader.GetString(10),
                NotificationStatus = reader.GetString(11),
                MaintenanceRelated = reader.GetInt32(12) == 1
            });
        }

        return rows;
    }

    private static async Task TransitionAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        int deviceId,
        AvailabilityStatus status,
        DateTime startedAtUtc,
        long? incidentId,
        string reasonCode,
        string reasonText,
        string detectionSource,
        DateTime? firstFailureAtUtc,
        DateTime? confirmedAtUtc,
        CancellationToken cancellationToken)
    {
        var startUtc = startedAtUtc.ToUniversalTime();
        var nowUtc = DateTime.UtcNow;
        var open = await GetOpenPeriodAsync(connection, transaction, deviceId, cancellationToken);
        if (open is not null && open.Status == status)
        {
            await using var update = connection.CreateCommand();
            update.Transaction = transaction;
            update.CommandText = """
                UPDATE DeviceAvailabilityPeriods
                SET IncidentId = COALESCE(@IncidentId, IncidentId),
                    ReasonCode = CASE WHEN @ReasonCode = '' THEN ReasonCode ELSE @ReasonCode END,
                    ReasonText = CASE WHEN @ReasonText = '' THEN ReasonText ELSE @ReasonText END,
                    DetectionSource = CASE WHEN @DetectionSource = '' THEN DetectionSource ELSE @DetectionSource END,
                    FirstFailureAtUtc = COALESCE(FirstFailureAtUtc, @FirstFailureAtUtc),
                    ConfirmedAtUtc = COALESCE(ConfirmedAtUtc, @ConfirmedAtUtc),
                    UpdatedAtUtc = @UpdatedAtUtc
                WHERE Id = @Id;
                """;
            AddParameter(update, "@IncidentId", incidentId);
            AddParameter(update, "@ReasonCode", reasonCode);
            AddParameter(update, "@ReasonText", reasonText);
            AddParameter(update, "@DetectionSource", detectionSource);
            AddParameter(update, "@FirstFailureAtUtc", firstFailureAtUtc.HasValue ? ToStorageDate(firstFailureAtUtc.Value) : null);
            AddParameter(update, "@ConfirmedAtUtc", confirmedAtUtc.HasValue ? ToStorageDate(confirmedAtUtc.Value) : null);
            AddParameter(update, "@UpdatedAtUtc", ToStorageDate(nowUtc));
            AddParameter(update, "@Id", open.Id);
            await update.ExecuteNonQueryAsync(cancellationToken);
            return;
        }

        if (open is not null)
        {
            var closeAtUtc = startUtc < open.StartedAtUtc ? open.StartedAtUtc : startUtc;
            await CloseOpenPeriodAsync(connection, transaction, open, closeAtUtc, cancellationToken);
            if (closeAtUtc == open.StartedAtUtc)
            {
                await DeletePeriodAsync(connection, transaction, open.Id, cancellationToken);
            }
        }

        await InsertOpenPeriodAsync(
            connection,
            transaction,
            deviceId,
            status,
            startUtc,
            incidentId,
            reasonCode,
            reasonText,
            detectionSource,
            firstFailureAtUtc,
            confirmedAtUtc,
            nowUtc,
            cancellationToken);
    }

    private static async Task MarkSuspectedFailureAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        int deviceId,
        DateTime firstFailureAtUtc,
        CancellationToken cancellationToken)
    {
        var open = await GetOpenPeriodAsync(connection, transaction, deviceId, cancellationToken);
        if (open is null)
        {
            await InsertOpenPeriodAsync(
                connection,
                transaction,
                deviceId,
                AvailabilityStatus.Unknown,
                firstFailureAtUtc,
                incidentId: null,
                "FirstFailure",
                "Ilk basarisiz ping; down henuz dogrulanmadi.",
                "WorkerPing",
                firstFailureAtUtc,
                confirmedAtUtc: null,
                DateTime.UtcNow,
                cancellationToken);
            return;
        }

        await using var update = connection.CreateCommand();
        update.Transaction = transaction;
        update.CommandText = """
            UPDATE DeviceAvailabilityPeriods
            SET FirstFailureAtUtc = COALESCE(FirstFailureAtUtc, @FirstFailureAtUtc),
                ReasonCode = CASE WHEN Status = 'Up' THEN 'FirstFailure' ELSE ReasonCode END,
                ReasonText = CASE WHEN Status = 'Up' THEN 'Ilk basarisiz ping; down henuz dogrulanmadi.' ELSE ReasonText END,
                UpdatedAtUtc = @UpdatedAtUtc
            WHERE Id = @Id;
            """;
        AddParameter(update, "@FirstFailureAtUtc", ToStorageDate(firstFailureAtUtc));
        AddParameter(update, "@UpdatedAtUtc", ToStorageDate(DateTime.UtcNow));
        AddParameter(update, "@Id", open.Id);
        await update.ExecuteNonQueryAsync(cancellationToken);
    }

    private static async Task CloseOpenPeriodAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        DeviceAvailabilityPeriod period,
        DateTime endedAtUtc,
        CancellationToken cancellationToken)
    {
        var endUtc = endedAtUtc.ToUniversalTime();
        var duration = Math.Max(0, (long)(endUtc - period.StartedAtUtc).TotalSeconds);
        await using var close = connection.CreateCommand();
        close.Transaction = transaction;
        close.CommandText = """
            UPDATE DeviceAvailabilityPeriods
            SET EndedAtUtc = @EndedAtUtc,
                DurationSeconds = @DurationSeconds,
                UpdatedAtUtc = @UpdatedAtUtc
            WHERE Id = @Id
              AND EndedAtUtc IS NULL;
            """;
        AddParameter(close, "@EndedAtUtc", ToStorageDate(endUtc));
        AddParameter(close, "@DurationSeconds", duration);
        AddParameter(close, "@UpdatedAtUtc", ToStorageDate(DateTime.UtcNow));
        AddParameter(close, "@Id", period.Id);
        await close.ExecuteNonQueryAsync(cancellationToken);
    }

    private static async Task DeletePeriodAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        long periodId,
        CancellationToken cancellationToken)
    {
        await using var delete = connection.CreateCommand();
        delete.Transaction = transaction;
        delete.CommandText = "DELETE FROM DeviceAvailabilityPeriods WHERE Id = @Id;";
        AddParameter(delete, "@Id", periodId);
        await delete.ExecuteNonQueryAsync(cancellationToken);
    }

    private static async Task InsertOpenPeriodAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        int deviceId,
        AvailabilityStatus status,
        DateTime startedAtUtc,
        long? incidentId,
        string reasonCode,
        string reasonText,
        string detectionSource,
        DateTime? firstFailureAtUtc,
        DateTime? confirmedAtUtc,
        DateTime nowUtc,
        CancellationToken cancellationToken)
    {
        await using var insert = connection.CreateCommand();
        insert.Transaction = transaction;
        insert.CommandText = """
            INSERT INTO DeviceAvailabilityPeriods
                (DeviceId, Status, StartedAtUtc, EndedAtUtc, DurationSeconds, IncidentId,
                 ReasonCode, ReasonText, DetectionSource, FirstFailureAtUtc, ConfirmedAtUtc,
                 CreatedAtUtc, UpdatedAtUtc)
            VALUES
                (@DeviceId, @Status, @StartedAtUtc, NULL, NULL, @IncidentId,
                 @ReasonCode, @ReasonText, @DetectionSource, @FirstFailureAtUtc, @ConfirmedAtUtc,
                 @CreatedAtUtc, @UpdatedAtUtc);
            """;
        AddParameter(insert, "@DeviceId", deviceId);
        AddParameter(insert, "@Status", status.ToStorageValue());
        AddParameter(insert, "@StartedAtUtc", ToStorageDate(startedAtUtc));
        AddParameter(insert, "@IncidentId", incidentId);
        AddParameter(insert, "@ReasonCode", reasonCode);
        AddParameter(insert, "@ReasonText", reasonText);
        AddParameter(insert, "@DetectionSource", detectionSource);
        AddParameter(insert, "@FirstFailureAtUtc", firstFailureAtUtc.HasValue ? ToStorageDate(firstFailureAtUtc.Value) : null);
        AddParameter(insert, "@ConfirmedAtUtc", confirmedAtUtc.HasValue ? ToStorageDate(confirmedAtUtc.Value) : null);
        AddParameter(insert, "@CreatedAtUtc", ToStorageDate(nowUtc));
        AddParameter(insert, "@UpdatedAtUtc", ToStorageDate(nowUtc));
        await insert.ExecuteNonQueryAsync(cancellationToken);
    }

    private static async Task<DeviceAvailabilityPeriod?> GetOpenPeriodAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        int deviceId,
        CancellationToken cancellationToken)
    {
        await using var select = connection.CreateCommand();
        select.Transaction = transaction;
        select.CommandText = """
            SELECT Id, DeviceId, Status, StartedAtUtc, EndedAtUtc, DurationSeconds, IncidentId,
                   ReasonCode, ReasonText, DetectionSource, FirstFailureAtUtc, ConfirmedAtUtc,
                   CreatedAtUtc, UpdatedAtUtc
            FROM DeviceAvailabilityPeriods
            WHERE DeviceId = @DeviceId
              AND EndedAtUtc IS NULL
            ORDER BY StartedAtUtc DESC
            LIMIT 1;
            """;
        AddParameter(select, "@DeviceId", deviceId);

        await using var reader = await select.ExecuteReaderAsync(cancellationToken);
        return await reader.ReadAsync(cancellationToken) ? ReadPeriod(reader) : null;
    }

    private static async Task<DateTime?> GetOpenFirstFailureAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        int deviceId,
        CancellationToken cancellationToken)
    {
        var open = await GetOpenPeriodAsync(connection, transaction, deviceId, cancellationToken);
        return open?.FirstFailureAtUtc;
    }

    private static async Task<long?> GetOpenIncidentIdAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        int deviceId,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            SELECT Id
            FROM DeviceIncidents
            WHERE DeviceId = @DeviceId
              AND Status = 'Open'
            ORDER BY StartedAtUtc DESC
            LIMIT 1;
            """;
        AddParameter(command, "@DeviceId", deviceId);
        var result = await command.ExecuteScalarAsync(cancellationToken);
        return result is null || result == DBNull.Value
            ? null
            : Convert.ToInt64(result, CultureInfo.InvariantCulture);
    }

    private static async Task<bool> IsInMaintenanceAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        int deviceId,
        int? groupId,
        DateTime atUtc,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            SELECT COUNT(1)
            FROM MaintenanceWindows w
            INNER JOIN MaintenanceWindowTargets t ON t.MaintenanceWindowId = w.Id
            WHERE w.Status IN ('Scheduled','Active')
              AND w.StartedAtUtc <= @AtUtc
              AND w.EndedAtUtc > @AtUtc
              AND (
                    t.TargetType = 'AllDevices'
                    OR (t.TargetType = 'Device' AND t.TargetId = @DeviceId)
                    OR (t.TargetType = 'Group' AND @GroupId IS NOT NULL AND t.TargetId = @GroupId)
                  );
            """;
        AddParameter(command, "@AtUtc", ToStorageDate(atUtc));
        AddParameter(command, "@DeviceId", deviceId);
        AddParameter(command, "@GroupId", groupId);
        var result = await command.ExecuteScalarAsync(cancellationToken);
        return Convert.ToInt32(result, CultureInfo.InvariantCulture) > 0;
    }

    private static async Task<IReadOnlyList<int>> GetActiveDeviceIdsAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        CancellationToken cancellationToken)
    {
        var ids = new List<int>();
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            SELECT Id
            FROM Devices
            WHERE IsDeleted = 0
              AND IsActive = 1
              AND IsEnabled = 1
              AND AutoCheckEnabled = 1;
            """;
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            ids.Add(reader.GetInt32(0));
        }

        return ids;
    }

    private static async Task<IReadOnlyList<int>> ResolveTargetDeviceIdsAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        MonitoringTargetType targetType,
        int? targetId,
        CancellationToken cancellationToken)
    {
        var ids = new List<int>();
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = targetType switch
        {
            MonitoringTargetType.Device => """
                SELECT Id
                FROM Devices
                WHERE Id = @TargetId
                  AND IsDeleted = 0
                  AND IsActive = 1
                  AND IsEnabled = 1;
                """,
            MonitoringTargetType.Group => """
                SELECT Id
                FROM Devices
                WHERE GroupId = @TargetId
                  AND IsDeleted = 0
                  AND IsActive = 1
                  AND IsEnabled = 1;
                """,
            _ => """
                SELECT Id
                FROM Devices
                WHERE IsDeleted = 0
                  AND IsActive = 1
                  AND IsEnabled = 1;
                """
        };
        AddParameter(command, "@TargetId", targetId);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            ids.Add(reader.GetInt32(0));
        }

        return ids;
    }

    private static async Task<IReadOnlyList<Device>> GetReportDevicesAsync(
        SqliteConnection connection,
        int? groupId,
        bool includeDeleted,
        CancellationToken cancellationToken)
    {
        var devices = new List<Device>();
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT Id, Name, IpAddress, DeviceType, Location, GroupId, GroupName, IsCritical,
                   IsActive, IsEnabled, IsDeleted, DeletedAtUtc, AutoCheckEnabled, DefaultSchedulePlanId,
                   PingTimeoutMs, CheckIntervalSeconds, FailureRetryIntervalSeconds, FailureRetryLimit,
                   FailureThreshold, Description, LastStatus, LastLatencyMs, LastCheckedAt,
                   LastSuccessfulCheckAt, LastFailedCheckAt, ConsecutiveFailures, ConsecutiveSuccesses,
                   LastStableStatus, CreatedAt, UpdatedAt, COALESCE(TargetAvailabilityPercent, GroupTargetAvailabilityPercent)
            FROM (
                SELECT d.*, g.TargetAvailabilityPercent AS GroupTargetAvailabilityPercent
                FROM Devices d
                LEFT JOIN DeviceGroups g ON g.Id = d.GroupId
            )
            WHERE (@IncludeDeleted = 1 OR IsDeleted = 0)
              AND (@GroupId IS NULL OR GroupId = @GroupId)
            ORDER BY Name COLLATE NOCASE, IpAddress;
            """;
        AddParameter(command, "@IncludeDeleted", includeDeleted ? 1 : 0);
        AddParameter(command, "@GroupId", groupId);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            devices.Add(new Device
            {
                Id = reader.GetInt32(0),
                Name = reader.GetString(1),
                IpAddress = reader.GetString(2),
                DeviceType = DeviceTypeExtensions.FromStorageValue(reader.GetString(3)),
                Location = reader.GetString(4),
                GroupId = reader.IsDBNull(5) ? null : reader.GetInt32(5),
                GroupName = reader.GetString(6),
                IsCritical = reader.GetInt32(7) == 1,
                IsActive = reader.GetInt32(8) == 1,
                IsEnabled = reader.GetInt32(9) == 1,
                IsDeleted = reader.GetInt32(10) == 1,
                DeletedAtUtc = reader.IsDBNull(11) ? null : FromStorageDate(reader.GetString(11)),
                AutoCheckEnabled = reader.GetInt32(12) == 1,
                DefaultSchedulePlanId = reader.IsDBNull(13) ? null : reader.GetInt32(13),
                PingTimeoutMs = reader.IsDBNull(14) ? null : reader.GetInt32(14),
                CheckIntervalSeconds = reader.GetInt32(15),
                FailureRetryIntervalSeconds = reader.GetInt32(16),
                FailureRetryLimit = reader.GetInt32(17),
                FailureThreshold = reader.GetInt32(18),
                Description = reader.GetString(19),
                LastStatus = DeviceStatusExtensions.FromStorageValue(reader.GetString(20)),
                LastLatencyMs = reader.IsDBNull(21) ? null : reader.GetInt64(21),
                LastCheckedAt = reader.IsDBNull(22) ? null : FromStorageDate(reader.GetString(22)),
                LastSuccessfulCheckAt = reader.IsDBNull(23) ? null : FromStorageDate(reader.GetString(23)),
                LastFailedCheckAt = reader.IsDBNull(24) ? null : FromStorageDate(reader.GetString(24)),
                ConsecutiveFailures = reader.GetInt32(25),
                ConsecutiveSuccesses = reader.GetInt32(26),
                LastStableStatus = DeviceStatusExtensions.FromStorageValue(reader.GetString(27)),
                CreatedAt = FromStorageDate(reader.GetString(28)),
                UpdatedAt = FromStorageDate(reader.GetString(29)),
                SlaTargetAvailabilityPercent = reader.IsDBNull(30) ? null : reader.GetDouble(30)
            });
        }

        return devices;
    }

    private static async Task<IReadOnlyList<DeviceAvailabilityPeriod>> GetPeriodsForDeviceAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        int deviceId,
        DateTime startUtc,
        DateTime endUtc,
        CancellationToken cancellationToken)
    {
        var periods = new List<DeviceAvailabilityPeriod>();
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            SELECT Id, DeviceId, Status, StartedAtUtc, EndedAtUtc, DurationSeconds, IncidentId,
                   ReasonCode, ReasonText, DetectionSource, FirstFailureAtUtc, ConfirmedAtUtc,
                   CreatedAtUtc, UpdatedAtUtc
            FROM DeviceAvailabilityPeriods
            WHERE DeviceId = @DeviceId
              AND StartedAtUtc < @EndUtc
              AND (EndedAtUtc IS NULL OR EndedAtUtc > @StartUtc)
            ORDER BY StartedAtUtc;
            """;
        AddParameter(command, "@DeviceId", deviceId);
        AddParameter(command, "@StartUtc", ToStorageDate(startUtc));
        AddParameter(command, "@EndUtc", ToStorageDate(endUtc));
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            periods.Add(ReadPeriod(reader));
        }

        return periods;
    }

    private static async Task<IReadOnlyList<(DateTime StartUtc, DateTime EndUtc)>> GetMonitoringIntervalsAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        Device device,
        DateOnly date,
        TimeZoneInfo timezone,
        DateTime dayStartUtc,
        DateTime dayEndUtc,
        CancellationToken cancellationToken)
    {
        var calendarId = await ResolveCalendarIdAsync(connection, transaction, device, cancellationToken);
        if (!calendarId.HasValue)
        {
            return new[] { (dayStartUtc, dayEndUtc) };
        }

        var rules = new List<(TimeSpan Start, TimeSpan End)>();
        await using (var command = connection.CreateCommand())
        {
            command.Transaction = transaction;
            command.CommandText = """
                SELECT StartTime, EndTime
                FROM MonitoringCalendarRules
                WHERE CalendarId = @CalendarId
                  AND DayOfWeek = @DayOfWeek
                  AND IsEnabled = 1;
                """;
            AddParameter(command, "@CalendarId", calendarId.Value);
            AddParameter(command, "@DayOfWeek", (int)date.DayOfWeek);
            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
            {
                if (TimeSpan.TryParse(reader.GetString(0), CultureInfo.InvariantCulture, out var start)
                    && TimeSpan.TryParse(reader.GetString(1), CultureInfo.InvariantCulture, out var end))
                {
                    rules.Add((start, end));
                }
            }
        }

        if (rules.Count == 0)
        {
            return new[] { (dayStartUtc, dayEndUtc) };
        }

        var intervals = new List<(DateTime StartUtc, DateTime EndUtc)>();
        foreach (var rule in rules)
        {
            var localStart = date.ToDateTime(TimeOnly.FromTimeSpan(rule.Start), DateTimeKind.Unspecified);
            var localEndDate = rule.End > rule.Start ? date : date.AddDays(1);
            var localEnd = localEndDate.ToDateTime(TimeOnly.FromTimeSpan(rule.End), DateTimeKind.Unspecified);
            var startUtc = TimeZoneInfo.ConvertTimeToUtc(localStart, timezone);
            var endUtc = TimeZoneInfo.ConvertTimeToUtc(localEnd, timezone);
            var clippedStart = Max(startUtc, dayStartUtc);
            var clippedEnd = Min(endUtc, dayEndUtc);
            if (clippedEnd > clippedStart)
            {
                intervals.Add((clippedStart, clippedEnd));
            }
        }

        return intervals;
    }

    private static async Task<long?> ResolveCalendarIdAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        Device device,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            SELECT CalendarId
            FROM DeviceMonitoringCalendarAssignments
            WHERE (TargetType = 'Device' AND TargetId = @DeviceId)
               OR (TargetType = 'Group' AND @GroupId IS NOT NULL AND TargetId = @GroupId)
               OR TargetType = 'AllDevices'
            ORDER BY CASE TargetType WHEN 'Device' THEN 0 WHEN 'Group' THEN 1 ELSE 2 END
            LIMIT 1;
            """;
        AddParameter(command, "@DeviceId", device.Id);
        AddParameter(command, "@GroupId", device.GroupId);
        var result = await command.ExecuteScalarAsync(cancellationToken);
        if (result is not null && result != DBNull.Value)
        {
            return Convert.ToInt64(result, CultureInfo.InvariantCulture);
        }

        await using var defaultCommand = connection.CreateCommand();
        defaultCommand.Transaction = transaction;
        defaultCommand.CommandText = "SELECT Id FROM MonitoringCalendars WHERE IsDefault = 1 ORDER BY Id LIMIT 1;";
        var defaultResult = await defaultCommand.ExecuteScalarAsync(cancellationToken);
        return defaultResult is null || defaultResult == DBNull.Value
            ? null
            : Convert.ToInt64(defaultResult, CultureInfo.InvariantCulture);
    }

    private static AvailabilityTotals CalculateTotals(
        IReadOnlyList<DeviceAvailabilityPeriod> periods,
        DateTime startUtc,
        DateTime endUtc)
    {
        long up = 0;
        long down = 0;
        long unknown = 0;
        long maintenance = 0;
        long paused = 0;
        long longestOutage = 0;
        long detectionDelay = 0;

        foreach (var period in periods)
        {
            var clippedStart = Max(period.StartedAtUtc, startUtc);
            var clippedEnd = Min(period.EndedAtUtc ?? endUtc, endUtc);
            var seconds = Math.Max(0, (long)(clippedEnd - clippedStart).TotalSeconds);
            if (seconds == 0)
            {
                continue;
            }

            switch (period.Status)
            {
                case AvailabilityStatus.Up:
                    up += seconds;
                    break;
                case AvailabilityStatus.Down:
                    down += seconds;
                    longestOutage = Math.Max(longestOutage, seconds);
                    if (period.FirstFailureAtUtc.HasValue && period.ConfirmedAtUtc.HasValue)
                    {
                        detectionDelay += Math.Max(0, (long)(period.ConfirmedAtUtc.Value - period.FirstFailureAtUtc.Value).TotalSeconds);
                    }

                    break;
                case AvailabilityStatus.Maintenance:
                    maintenance += seconds;
                    break;
                case AvailabilityStatus.Paused:
                    paused += seconds;
                    break;
                default:
                    unknown += seconds;
                    break;
            }
        }

        var expected = Math.Max(0, (long)(endUtc - startUtc).TotalSeconds);
        var accounted = up + down + unknown + maintenance + paused;
        if (accounted < expected)
        {
            unknown += expected - accounted;
        }

        var known = up + down;
        var strict = up + down + unknown;
        return new AvailabilityTotals(
            up,
            down,
            unknown,
            maintenance,
            paused,
            longestOutage,
            detectionDelay,
            known == 0 ? null : up * 100d / known,
            strict == 0 ? null : up * 100d / strict,
            expected == 0 ? null : known * 100d / expected);
    }

    private static AvailabilityTotals SumTotals(IEnumerable<AvailabilityTotals> totals)
    {
        var materialized = totals.ToList();
        var up = materialized.Sum(item => item.UpSeconds);
        var down = materialized.Sum(item => item.DownSeconds);
        var unknown = materialized.Sum(item => item.UnknownSeconds);
        var maintenance = materialized.Sum(item => item.MaintenanceSeconds);
        var paused = materialized.Sum(item => item.PausedSeconds);
        var longest = materialized.Count == 0 ? 0 : materialized.Max(item => item.LongestOutageSeconds);
        var delay = materialized.Sum(item => item.TotalDetectionDelaySeconds);
        var expected = up + down + unknown + maintenance + paused;
        var known = up + down;
        var strict = up + down + unknown;
        return new AvailabilityTotals(
            up,
            down,
            unknown,
            maintenance,
            paused,
            longest,
            delay,
            known == 0 ? null : up * 100d / known,
            strict == 0 ? null : up * 100d / strict,
            expected == 0 ? null : known * 100d / expected);
    }

    private static async Task<Dictionary<int, IncidentStats>> GetIncidentStatsAsync(
        SqliteConnection connection,
        DateTime startUtc,
        DateTime endUtc,
        CancellationToken cancellationToken)
    {
        var stats = new Dictionary<int, IncidentStats>();
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT DeviceId, StartedAtUtc, EndedAtUtc, Status
            FROM DeviceIncidents
            WHERE StartedAtUtc < @EndUtc
              AND COALESCE(EndedAtUtc, @EndUtc) > @StartUtc;
            """;
        AddParameter(command, "@StartUtc", ToStorageDate(startUtc));
        AddParameter(command, "@EndUtc", ToStorageDate(endUtc));
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            var deviceId = reader.GetInt32(0);
            var started = FromStorageDate(reader.GetString(1));
            var recovered = reader.IsDBNull(2) ? null : (DateTime?)FromStorageDate(reader.GetString(2));
            var clippedEnd = recovered ?? endUtc;
            var downtime = Math.Max(0, (long)(Min(clippedEnd, endUtc) - Max(started, startUtc)).TotalSeconds);
            stats.TryGetValue(deviceId, out var existing);
            var recoveredCount = existing.RecoveredIncidentCount + (recovered.HasValue ? 1 : 0);
            var closedDowntime = existing.ClosedIncidentDowntimeSeconds + (recovered.HasValue ? downtime : 0);
            stats[deviceId] = new IncidentStats(
                existing.IncidentCount + 1,
                recoveredCount,
                closedDowntime,
                recoveredCount == 0 ? 0 : closedDowntime / recoveredCount,
                Math.Max(existing.LongestOutageSeconds, downtime));
        }

        return stats;
    }

    private static async Task UpsertDailyAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        int deviceId,
        DateOnly date,
        string timezoneId,
        long expectedSeconds,
        AvailabilityTotals totals,
        IncidentStats incidentStats,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            INSERT INTO DeviceAvailabilityDaily
                (DeviceId, Date, TimezoneId, ExpectedMonitoringSeconds, UpSeconds, DownSeconds,
                 UnknownSeconds, MaintenanceSeconds, PausedSeconds, IncidentCount, RecoveredIncidentCount,
                 LongestOutageSeconds, TotalDetectionDelaySeconds, AvailabilityPercent,
                 StrictAvailabilityPercent, CoveragePercent, CalculatedAtUtc, CalculationVersion)
            VALUES
                (@DeviceId, @Date, @TimezoneId, @ExpectedMonitoringSeconds, @UpSeconds, @DownSeconds,
                 @UnknownSeconds, @MaintenanceSeconds, @PausedSeconds, @IncidentCount, @RecoveredIncidentCount,
                 @LongestOutageSeconds, @TotalDetectionDelaySeconds, @AvailabilityPercent,
                 @StrictAvailabilityPercent, @CoveragePercent, @CalculatedAtUtc, @CalculationVersion)
            ON CONFLICT(DeviceId, Date, TimezoneId) DO UPDATE SET
                ExpectedMonitoringSeconds = excluded.ExpectedMonitoringSeconds,
                UpSeconds = excluded.UpSeconds,
                DownSeconds = excluded.DownSeconds,
                UnknownSeconds = excluded.UnknownSeconds,
                MaintenanceSeconds = excluded.MaintenanceSeconds,
                PausedSeconds = excluded.PausedSeconds,
                IncidentCount = excluded.IncidentCount,
                RecoveredIncidentCount = excluded.RecoveredIncidentCount,
                LongestOutageSeconds = excluded.LongestOutageSeconds,
                TotalDetectionDelaySeconds = excluded.TotalDetectionDelaySeconds,
                AvailabilityPercent = excluded.AvailabilityPercent,
                StrictAvailabilityPercent = excluded.StrictAvailabilityPercent,
                CoveragePercent = excluded.CoveragePercent,
                CalculatedAtUtc = excluded.CalculatedAtUtc,
                CalculationVersion = excluded.CalculationVersion;
            """;
        AddParameter(command, "@DeviceId", deviceId);
        AddParameter(command, "@Date", date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));
        AddParameter(command, "@TimezoneId", timezoneId);
        AddParameter(command, "@ExpectedMonitoringSeconds", expectedSeconds);
        AddParameter(command, "@UpSeconds", totals.UpSeconds);
        AddParameter(command, "@DownSeconds", totals.DownSeconds);
        AddParameter(command, "@UnknownSeconds", totals.UnknownSeconds);
        AddParameter(command, "@MaintenanceSeconds", totals.MaintenanceSeconds);
        AddParameter(command, "@PausedSeconds", totals.PausedSeconds);
        AddParameter(command, "@IncidentCount", incidentStats.IncidentCount);
        AddParameter(command, "@RecoveredIncidentCount", incidentStats.RecoveredIncidentCount);
        AddParameter(command, "@LongestOutageSeconds", Math.Max(totals.LongestOutageSeconds, incidentStats.LongestOutageSeconds));
        AddParameter(command, "@TotalDetectionDelaySeconds", totals.TotalDetectionDelaySeconds);
        AddParameter(command, "@AvailabilityPercent", totals.AvailabilityPercent);
        AddParameter(command, "@StrictAvailabilityPercent", totals.StrictAvailabilityPercent);
        AddParameter(command, "@CoveragePercent", totals.CoveragePercent);
        AddParameter(command, "@CalculatedAtUtc", ToStorageDate(DateTime.UtcNow));
        AddParameter(command, "@CalculationVersion", CalculationVersion);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static async Task InsertAuditAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        string operationType,
        int? deviceId,
        int? groupId,
        DateOnly startDate,
        DateOnly endDate,
        string result,
        string message,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            INSERT INTO AvailabilityRecalculationAudits
                (RequestedAtUtc, OperationType, DeviceId, GroupId, StartDate, EndDate, RequestedBy, Result, Message)
            VALUES
                (@RequestedAtUtc, @OperationType, @DeviceId, @GroupId, @StartDate, @EndDate, @RequestedBy, @Result, @Message);
            """;
        AddParameter(command, "@RequestedAtUtc", ToStorageDate(DateTime.UtcNow));
        AddParameter(command, "@OperationType", operationType);
        AddParameter(command, "@DeviceId", deviceId);
        AddParameter(command, "@GroupId", groupId);
        AddParameter(command, "@StartDate", startDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));
        AddParameter(command, "@EndDate", endDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));
        AddParameter(command, "@RequestedBy", Environment.UserName);
        AddParameter(command, "@Result", result);
        AddParameter(command, "@Message", message);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static string CalculateSlaStatus(double? targetPercent, double? actualPercent)
    {
        if (!targetPercent.HasValue)
        {
            return "Tanimli degil";
        }

        if (!actualPercent.HasValue)
        {
            return "Veri yok";
        }

        return actualPercent.Value + 0.000001 >= targetPercent.Value ? "Uygun" : "Ihlal";
    }

    private static double? WeightedAvailability(IEnumerable<AvailabilitySummaryReportItem> rows)
    {
        var materialized = rows.ToList();
        var up = materialized.Sum(row => row.UpSeconds);
        var known = materialized.Sum(row => row.UpSeconds + row.DownSeconds);
        return known == 0 ? null : up * 100d / known;
    }

    private static double? WeightedCoverage(IEnumerable<AvailabilitySummaryReportItem> rows)
    {
        var materialized = rows.ToList();
        var known = materialized.Sum(row => row.UpSeconds + row.DownSeconds);
        var expected = materialized.Sum(row => row.ExpectedMonitoringSeconds);
        return expected == 0 ? null : known * 100d / expected;
    }

    private static TimeZoneInfo ResolveTimezone(string timezoneId)
    {
        try
        {
            return TimeZoneInfo.FindSystemTimeZoneById(string.IsNullOrWhiteSpace(timezoneId) ? TimeZoneInfo.Local.Id : timezoneId);
        }
        catch (TimeZoneNotFoundException)
        {
            return TimeZoneInfo.Local;
        }
        catch (InvalidTimeZoneException)
        {
            return TimeZoneInfo.Local;
        }
    }

    private static DeviceAvailabilityPeriod ReadPeriod(SqliteDataReader reader)
    {
        return new DeviceAvailabilityPeriod
        {
            Id = reader.GetInt64(0),
            DeviceId = reader.GetInt32(1),
            Status = AvailabilityStatusExtensions.FromStorageValue(reader.GetString(2)),
            StartedAtUtc = FromStorageDate(reader.GetString(3)),
            EndedAtUtc = reader.IsDBNull(4) ? null : FromStorageDate(reader.GetString(4)),
            DurationSeconds = reader.IsDBNull(5) ? null : reader.GetInt64(5),
            IncidentId = reader.IsDBNull(6) ? null : reader.GetInt64(6),
            ReasonCode = reader.GetString(7),
            ReasonText = reader.GetString(8),
            DetectionSource = reader.GetString(9),
            FirstFailureAtUtc = reader.IsDBNull(10) ? null : FromStorageDate(reader.GetString(10)),
            ConfirmedAtUtc = reader.IsDBNull(11) ? null : FromStorageDate(reader.GetString(11)),
            CreatedAtUtc = FromStorageDate(reader.GetString(12)),
            UpdatedAtUtc = FromStorageDate(reader.GetString(13))
        };
    }

    private static DateTime Min(DateTime first, DateTime second)
    {
        return first <= second ? first : second;
    }

    private static DateTime Max(DateTime first, DateTime second)
    {
        return first >= second ? first : second;
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

    private static MonitoringTargetType ParseTargetType(string value)
    {
        return Enum.TryParse<MonitoringTargetType>(value, true, out var parsed)
            ? parsed
            : MonitoringTargetType.AllDevices;
    }

    private readonly record struct AvailabilityTotals(
        long UpSeconds,
        long DownSeconds,
        long UnknownSeconds,
        long MaintenanceSeconds,
        long PausedSeconds,
        long LongestOutageSeconds,
        long TotalDetectionDelaySeconds,
        double? AvailabilityPercent,
        double? StrictAvailabilityPercent,
        double? CoveragePercent);

    private readonly record struct IncidentStats(
        int IncidentCount,
        int RecoveredIncidentCount,
        long ClosedIncidentDowntimeSeconds,
        long MttrSeconds,
        long LongestOutageSeconds);
}
