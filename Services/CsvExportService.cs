using System.Globalization;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Text;
using NetworkHealthMonitor.Models;

namespace NetworkHealthMonitor.Services;

public sealed class CsvExportService
{
    private static readonly UTF8Encoding Utf8Bom = new(encoderShouldEmitUTF8Identifier: true);

    public async Task ExportDevicesAsync(IEnumerable<Device> devices, string filePath, string delimiter = AppSettings.DefaultCsvDelimiter)
    {
        var separator = NormalizeDelimiter(delimiter);
        var builder = new StringBuilder();
        AppendRow(
            builder,
            separator,
            "Name",
            "IpAddress",
            "DeviceType",
            "GroupName",
            "Location",
            "Description",
            "IsCritical",
            "IsEnabled",
            "AutoCheckEnabled",
            "PingTimeoutMs",
            "CheckIntervalSeconds",
            "RetryIntervalSeconds",
            "RetryLimit",
            "FailureThreshold");

        foreach (var device in devices)
        {
            AppendRow(
                builder,
                separator,
                device.Name,
                device.IpAddress,
                device.DeviceTypeText,
                device.GroupName,
                device.Location,
                device.Description,
                device.IsCritical.ToString().ToLowerInvariant(),
                device.IsEnabled.ToString().ToLowerInvariant(),
                device.AutoCheckEnabled.ToString().ToLowerInvariant(),
                device.PingTimeoutMs?.ToString(CultureInfo.InvariantCulture) ?? string.Empty,
                device.CheckIntervalSeconds.ToString(CultureInfo.InvariantCulture),
                device.FailureRetryIntervalSeconds.ToString(CultureInfo.InvariantCulture),
                device.FailureRetryLimit.ToString(CultureInfo.InvariantCulture),
                device.FailureThreshold.ToString(CultureInfo.InvariantCulture));
        }

        await File.WriteAllTextAsync(filePath, builder.ToString(), Utf8Bom);
    }

    public async Task ExportDeviceTemplateAsync(string filePath, string delimiter = AppSettings.DefaultCsvDelimiter)
    {
        var separator = NormalizeDelimiter(delimiter);
        var builder = new StringBuilder();
        AppendRow(
            builder,
            separator,
            "Name",
            "IpAddress",
            "DeviceType",
            "GroupName",
            "Location",
            "Description",
            "IsCritical",
            "IsEnabled",
            "AutoCheckEnabled",
            "PingTimeoutMs",
            "CheckIntervalSeconds",
            "RetryIntervalSeconds",
            "RetryLimit",
            "FailureThreshold");
        AppendRow(builder, separator, "Kamera 1", "192.168.1.10", "Kamera", "Kameralar", "Depo", "Ornek kamera", "false", "true", "true", "", "0", "0", "0", "0");
        AppendRow(
            builder,
            separator,
            "Switch Ana",
            "192.168.1.2",
            "Switch",
            "Switchler",
            "Sistem Odasi",
            "Ornek switch",
            "true",
            "true",
            "true",
            AppSettings.DefaultPingTimeoutMs.ToString(CultureInfo.InvariantCulture),
            (AppSettings.DefaultAutoCheckIntervalMinutes * 60).ToString(CultureInfo.InvariantCulture),
            AppSettings.DefaultFailureRetryIntervalSecondsValue.ToString(CultureInfo.InvariantCulture),
            AppSettings.DefaultFailureRetryLimitValue.ToString(CultureInfo.InvariantCulture),
            AppSettings.DefaultFailureThresholdValue.ToString(CultureInfo.InvariantCulture));
        await File.WriteAllTextAsync(filePath, builder.ToString(), Utf8Bom);
    }

    public async Task ExportLogsAsync(IEnumerable<PingLog> logs, string filePath, string delimiter = AppSettings.DefaultCsvDelimiter)
    {
        var separator = NormalizeDelimiter(delimiter);
        var builder = new StringBuilder();
        AppendRow(builder, separator, "Tarih/Saat", "Cihaz adi", "IP adresi", "Cihaz tipi", "Durum", "Gecikme", "Hata mesaji");

        foreach (var log in logs)
        {
            AppendRow(builder, separator, log.CheckedAtText, log.DeviceName, log.IpAddress, log.DeviceTypeText, log.StatusText, log.LatencyText, log.ErrorMessage);
        }

        await File.WriteAllTextAsync(filePath, builder.ToString(), Utf8Bom);
    }

    public async Task ExportAvailabilityAsync(IEnumerable<AvailabilityReportItem> items, string filePath, string delimiter = AppSettings.DefaultCsvDelimiter)
    {
        var separator = NormalizeDelimiter(delimiter);
        var builder = new StringBuilder();
        AppendRow(
            builder,
            separator,
            "Cihaz adi",
            "IP adresi",
            "Cihaz tipi",
            "Grup",
            "Son durum",
            "Son basarili kontrol",
            "Son basarisiz kontrol",
            "Basarili kontrol",
            "Basarisiz kontrol",
            "Olculen erisilebilirlik",
            "Son 24 saat",
            "Son 7 gun",
            "Son 30 gun",
            "Kesinti sayisi",
            "Son kesinti baslangici",
            "Son toparlanma",
            "Tahmini kesinti suresi");

        foreach (var item in items)
        {
            AppendRow(
                builder,
                separator,
                item.DeviceName,
                item.IpAddress,
                item.DeviceTypeText,
                item.GroupName,
                item.LastStatusText,
                item.LastSuccessfulCheckAtText,
                item.LastFailedCheckAtText,
                item.TotalSuccessCount.ToString(CultureInfo.InvariantCulture),
                item.TotalFailureCount.ToString(CultureInfo.InvariantCulture),
                item.MeasuredAvailabilityText,
                item.Availability24HoursText,
                item.Availability7DaysText,
                item.Availability30DaysText,
                item.OutageCount.ToString(CultureInfo.InvariantCulture),
                item.LastOutageStartedAtText,
                item.LastRecoveryAtText,
                item.EstimatedOutageDurationText);
        }

        await File.WriteAllTextAsync(filePath, builder.ToString(), Utf8Bom);
    }

    public async Task ExportUptimeReportAsync(IEnumerable<UptimeReportItem> items, string filePath, string delimiter = AppSettings.DefaultCsvDelimiter)
    {
        var separator = NormalizeDelimiter(delimiter);
        var builder = new StringBuilder();
        AppendRow(
            builder,
            separator,
            "DeviceId",
            "DeviceName",
            "IpAddress",
            "DeviceType",
            "GroupName",
            "HealthStatus",
            "LastCheckAt",
            "LastSuccessfulCheckAt",
            "LastFailedCheckAt",
            "LatencyMs",
            "ConsecutiveFailureCount",
            "Uptime24hPercent",
            "Uptime7dPercent",
            "Uptime30dPercent",
            "UptimeOverallPercent",
            "TotalChecks24h",
            "SuccessfulChecks24h",
            "FailedChecks24h",
            "TotalChecks7d",
            "SuccessfulChecks7d",
            "FailedChecks7d",
            "TotalChecks30d",
            "SuccessfulChecks30d",
            "FailedChecks30d",
            "TotalChecksOverall",
            "SuccessfulChecksOverall",
            "FailedChecksOverall");

        foreach (var item in items)
        {
            AppendRow(
                builder,
                separator,
                item.DeviceId.ToString(CultureInfo.InvariantCulture),
                item.DeviceName,
                item.IpAddress,
                item.DeviceTypeText,
                item.GroupName,
                item.HealthStatusText,
                item.LastCheckAtText,
                item.LastSuccessfulCheckAtText,
                item.LastFailedCheckAtText,
                item.LatencyMsText,
                item.ConsecutiveFailureCount.ToString(CultureInfo.InvariantCulture),
                FormatPercent(item.Uptime24hPercent),
                FormatPercent(item.Uptime7dPercent),
                FormatPercent(item.Uptime30dPercent),
                FormatPercent(item.UptimeOverallPercent),
                item.TotalChecks24h.ToString(CultureInfo.InvariantCulture),
                item.SuccessfulChecks24h.ToString(CultureInfo.InvariantCulture),
                item.FailedChecks24h.ToString(CultureInfo.InvariantCulture),
                item.TotalChecks7d.ToString(CultureInfo.InvariantCulture),
                item.SuccessfulChecks7d.ToString(CultureInfo.InvariantCulture),
                item.FailedChecks7d.ToString(CultureInfo.InvariantCulture),
                item.TotalChecks30d.ToString(CultureInfo.InvariantCulture),
                item.SuccessfulChecks30d.ToString(CultureInfo.InvariantCulture),
                item.FailedChecks30d.ToString(CultureInfo.InvariantCulture),
                item.TotalChecksOverall.ToString(CultureInfo.InvariantCulture),
                item.SuccessfulChecksOverall.ToString(CultureInfo.InvariantCulture),
                item.FailedChecksOverall.ToString(CultureInfo.InvariantCulture));
        }

        await File.WriteAllTextAsync(filePath, builder.ToString(), Utf8Bom);
    }

    public async Task ExportAvailabilitySummaryAsync(
        IEnumerable<AvailabilitySummaryReportItem> items,
        string filePath,
        string delimiter = AppSettings.DefaultCsvDelimiter)
    {
        var separator = NormalizeDelimiter(delimiter);
        var builder = new StringBuilder();
        AppendRow(
            builder,
            separator,
            "DeviceId",
            "Cihaz Adi",
            "IP Adresi",
            "Cihaz Turu",
            "Grup",
            "Rapor Baslangici UTC",
            "Rapor Bitisi UTC",
            "Timezone",
            "Beklenen Izleme Suresi",
            "UpSeconds",
            "Erisilebilir Sure",
            "DownSeconds",
            "Kesinti Suresi",
            "UnknownSeconds",
            "Bilinmeyen Sure",
            "MaintenanceSeconds",
            "Bakim Suresi",
            "AvailabilityPercent",
            "StrictAvailabilityPercent",
            "CoveragePercent",
            "IncidentCount",
            "RecoveredIncidentCount",
            "MTTRSeconds",
            "MTTR",
            "MTBFSeconds",
            "MTBF",
            "LongestOutageSeconds",
            "En Uzun Kesinti",
            "CurrentStatus",
            "CurrentStatusSince",
            "CurrentContinuousAvailabilitySeconds",
            "Kesintisiz Erisilebilir Sure",
            "LastCheckedAt",
            "LastSuccessfulCheckAt",
            "SlaTargetPercent",
            "SlaStatus");

        foreach (var item in items)
        {
            AppendRow(
                builder,
                separator,
                item.DeviceId.ToString(CultureInfo.InvariantCulture),
                item.DeviceName,
                item.IpAddress,
                item.DeviceTypeText,
                item.GroupName,
                FormatUtc(item.ReportStartUtc),
                FormatUtc(item.ReportEndUtc),
                item.TimezoneId,
                item.ExpectedMonitoringSeconds.ToString(CultureInfo.InvariantCulture),
                item.UpSeconds.ToString(CultureInfo.InvariantCulture),
                AvailabilitySummaryReportItem.FormatDuration(item.UpSeconds),
                item.DownSeconds.ToString(CultureInfo.InvariantCulture),
                AvailabilitySummaryReportItem.FormatDuration(item.DownSeconds),
                item.UnknownSeconds.ToString(CultureInfo.InvariantCulture),
                AvailabilitySummaryReportItem.FormatDuration(item.UnknownSeconds),
                item.MaintenanceSeconds.ToString(CultureInfo.InvariantCulture),
                AvailabilitySummaryReportItem.FormatDuration(item.MaintenanceSeconds),
                FormatInvariantPercent(item.AvailabilityPercent),
                FormatInvariantPercent(item.StrictAvailabilityPercent),
                FormatInvariantPercent(item.CoveragePercent),
                item.IncidentCount.ToString(CultureInfo.InvariantCulture),
                item.RecoveredIncidentCount.ToString(CultureInfo.InvariantCulture),
                item.MttrSeconds.ToString(CultureInfo.InvariantCulture),
                AvailabilitySummaryReportItem.FormatDuration(item.MttrSeconds),
                item.MtbfSeconds.ToString(CultureInfo.InvariantCulture),
                AvailabilitySummaryReportItem.FormatDuration(item.MtbfSeconds),
                item.LongestOutageSeconds.ToString(CultureInfo.InvariantCulture),
                AvailabilitySummaryReportItem.FormatDuration(item.LongestOutageSeconds),
                item.CurrentStatusText,
                FormatNullableUtc(item.CurrentStatusSinceUtc),
                item.CurrentContinuousAvailabilitySeconds.ToString(CultureInfo.InvariantCulture),
                item.CurrentContinuousAvailabilityText,
                FormatNullableUtc(item.LastCheckedAtUtc),
                FormatNullableUtc(item.LastSuccessfulCheckAtUtc),
                item.SlaTargetPercent?.ToString("0.###", CultureInfo.InvariantCulture) ?? string.Empty,
                item.SlaStatus);
        }

        await File.WriteAllTextAsync(filePath, builder.ToString(), Utf8Bom);
    }

    public async Task ExportAvailabilityIncidentsAsync(
        IEnumerable<AvailabilityIncidentReportItem> items,
        string filePath,
        string delimiter = AppSettings.DefaultCsvDelimiter)
    {
        var separator = NormalizeDelimiter(delimiter);
        var builder = new StringBuilder();
        AppendRow(
            builder,
            separator,
            "IncidentId",
            "DeviceId",
            "Cihaz",
            "IP",
            "Grup",
            "FirstFailureAtUtc",
            "ConfirmedDownAtUtc",
            "RecoveredAtUtc",
            "DowntimeSeconds",
            "DetectionDelaySeconds",
            "FailureCount",
            "ErrorCode",
            "ErrorMessage",
            "NotificationStatus",
            "MaintenanceRelated");

        foreach (var item in items)
        {
            AppendRow(
                builder,
                separator,
                item.IncidentId.ToString(CultureInfo.InvariantCulture),
                item.DeviceId.ToString(CultureInfo.InvariantCulture),
                item.DeviceName,
                item.IpAddress,
                item.GroupName,
                FormatUtc(item.FirstFailureAtUtc),
                FormatUtc(item.ConfirmedDownAtUtc),
                FormatNullableUtc(item.RecoveredAtUtc),
                item.DowntimeSeconds.ToString(CultureInfo.InvariantCulture),
                item.DetectionDelaySeconds.ToString(CultureInfo.InvariantCulture),
                item.FailureCount.ToString(CultureInfo.InvariantCulture),
                item.ErrorCode,
                item.ErrorMessage,
                item.NotificationStatus,
                item.MaintenanceRelated ? "true" : "false");
        }

        await File.WriteAllTextAsync(filePath, builder.ToString(), Utf8Bom);
    }

    public async Task ExportImportErrorsAsync(IEnumerable<CsvImportError> errors, string filePath, string delimiter = AppSettings.DefaultCsvDelimiter)
    {
        var separator = NormalizeDelimiter(delimiter);
        var builder = new StringBuilder();
        AppendRow(builder, separator, "RowNumber", "Name", "IpAddress", "DeviceType", "GroupName", "Location", "Description", "AutoCheckEnabled", "CheckIntervalSeconds", "RetryIntervalSeconds", "RetryLimit", "Error");

        foreach (var error in errors)
        {
            AppendRow(
                builder,
                separator,
                error.RowNumber.ToString(CultureInfo.InvariantCulture),
                error.Name,
                error.IpAddress,
                error.DeviceType,
                error.GroupName,
                error.Location,
                error.Description,
                error.AutoCheckEnabled,
                error.CheckIntervalSeconds,
                error.RetryIntervalSeconds,
                error.RetryLimit,
                error.Error);
        }

        await File.WriteAllTextAsync(filePath, builder.ToString(), Utf8Bom);
    }

    public async Task<CsvImportPreview> ReadDeviceImportPreviewAsync(
        string filePath,
        IEnumerable<Device> existingDevices,
        string delimiter = AppSettings.DefaultCsvDelimiter)
    {
        return await ReadDeviceImportPreviewAsync(
            filePath,
            existingDevices,
            new CsvImportOptions(CsvImportMode.Upsert, CsvImportScope.AllActiveDevices, null, string.Empty, Path.GetFileName(filePath), Environment.UserName),
            delimiter);
    }

    public async Task<CsvImportPreview> ReadDeviceImportPreviewAsync(
        string filePath,
        IEnumerable<Device> existingDevices,
        CsvImportOptions options,
        string delimiter = AppSettings.DefaultCsvDelimiter)
    {
        var separator = NormalizeDelimiter(delimiter);
        var lines = await File.ReadAllLinesAsync(filePath, Encoding.UTF8);
        var rows = new List<CsvImportPreviewRow>();
        var existingByIp = existingDevices
            .GroupBy(device => NormalizeIp(device.IpAddress) ?? device.IpAddress.Trim(), StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.ToList(), StringComparer.OrdinalIgnoreCase);

        if (lines.Length == 0)
        {
            rows.Add(CreateInvalidRow(1, string.Empty, string.Empty, string.Empty, string.Empty, string.Empty, string.Empty, string.Empty, string.Empty, string.Empty, string.Empty, "CSV dosyasi bos."));
            return new CsvImportPreview(0, rows);
        }

        var headers = ParseRow(lines[0], separator);
        var indexes = BuildHeaderIndex(headers);
        var missingColumns = new[] { "Name", "IpAddress", "DeviceType" }
            .Where(column => !indexes.ContainsKey(column))
            .ToList();

        if (missingColumns.Count > 0)
        {
            rows.Add(CreateInvalidRow(1, string.Empty, string.Empty, string.Empty, string.Empty, string.Empty, string.Empty, string.Empty, string.Empty, string.Empty, string.Empty, $"Zorunlu kolon eksik: {string.Join(", ", missingColumns)}"));
            return new CsvImportPreview(Math.Max(0, lines.Length - 1), rows);
        }

        var seenCsvIps = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var duplicateIps = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var totalRows = 0;

        for (var lineIndex = 1; lineIndex < lines.Length; lineIndex++)
        {
            if (string.IsNullOrWhiteSpace(lines[lineIndex]))
            {
                continue;
            }

            totalRows++;
            var rowNumber = lineIndex + 1;
            var values = ParseRow(lines[lineIndex], separator);
            var name = GetValue(values, indexes, "Name").Trim();
            var ipText = GetValue(values, indexes, "IpAddress").Trim();
            var deviceTypeText = GetValue(values, indexes, "DeviceType").Trim();
            var rowGroupName = GetValue(values, indexes, "GroupName").Trim();
            var groupName = options.Scope == CsvImportScope.SelectedGroup && string.IsNullOrWhiteSpace(rowGroupName)
                ? options.GroupName.Trim()
                : rowGroupName;
            var location = GetValue(values, indexes, "Location").Trim();
            var description = GetValue(values, indexes, "Description").Trim();
            var isCriticalText = GetValue(values, indexes, "IsCritical").Trim();
            var isEnabledText = GetValue(values, indexes, "IsEnabled").Trim();
            var autoCheckText = GetValue(values, indexes, "AutoCheckEnabled").Trim();
            var pingTimeoutText = GetValue(values, indexes, "PingTimeoutMs").Trim();
            var checkIntervalText = GetValue(values, indexes, "CheckIntervalSeconds").Trim();
            var retryIntervalText = GetValue(values, indexes, "RetryIntervalSeconds").Trim();
            var retryLimitText = GetValue(values, indexes, "RetryLimit").Trim();
            var failureThresholdText = GetValue(values, indexes, "FailureThreshold").Trim();

            var error = ValidateImportRow(
                name,
                ipText,
                deviceTypeText,
                isCriticalText,
                isEnabledText,
                autoCheckText,
                pingTimeoutText,
                checkIntervalText,
                retryIntervalText,
                retryLimitText,
                failureThresholdText,
                out var normalizedIp,
                out var deviceType,
                out var isCritical,
                out var isEnabled,
                out var autoCheckEnabled,
                out var pingTimeoutMs,
                out var checkIntervalSeconds,
                out var retryIntervalSeconds,
                out var retryLimit,
                out var failureThreshold);

            if (error is not null)
            {
                rows.Add(CreateInvalidRow(rowNumber, name, ipText, deviceTypeText, groupName, location, description, autoCheckText, checkIntervalText, retryIntervalText, retryLimitText, error));
                continue;
            }

            if (!seenCsvIps.Add(normalizedIp))
            {
                duplicateIps.Add(normalizedIp);
                rows.Add(CreateDuplicateRow(rowNumber, name, normalizedIp, deviceType, groupName, location, description, autoCheckEnabled, checkIntervalSeconds, retryIntervalSeconds, retryLimit, "Ayni CSV icinde duplicate IP var."));
                continue;
            }

            var record = new DeviceCsvRecord(rowNumber, name, normalizedIp, deviceType, groupName, location, description, isCritical, isEnabled, autoCheckEnabled, pingTimeoutMs, checkIntervalSeconds, retryIntervalSeconds, retryLimit, failureThreshold);
            var existingMatches = existingByIp.TryGetValue(normalizedIp, out var matches) ? matches : new List<Device>();
            if (existingMatches.Count(device => !device.IsDeleted) > 1 || (existingMatches.Any(device => !device.IsDeleted) && existingMatches.Any(device => device.IsDeleted)))
            {
                rows.Add(CreateDuplicateRow(rowNumber, name, normalizedIp, deviceType, groupName, location, description, autoCheckEnabled, checkIntervalSeconds, retryIntervalSeconds, retryLimit, "Ayni IP icin aktif/silinmis tutarsiz veritabani kaydi var."));
                continue;
            }

            var existing = existingMatches.OrderBy(device => device.IsDeleted).FirstOrDefault();
            var status = DetermineRowStatus(record, existing, options);
            rows.Add(new CsvImportPreviewRow
            {
                RowNumber = rowNumber,
                Name = name,
                IpAddress = normalizedIp,
                DeviceType = deviceType.ToDisplayName(),
                GroupName = groupName,
                Location = location,
                Description = description,
                AutoCheckEnabled = autoCheckEnabled,
                CheckIntervalSeconds = checkIntervalSeconds,
                RetryIntervalSeconds = retryIntervalSeconds,
                RetryLimit = retryLimit,
                Status = status,
                ChangeSummary = existing is null ? string.Empty : DescribeChanges(record, existing),
                Record = record,
                ExistsInDatabase = existing is not null,
                ExistingDeviceId = existing?.Id,
                ExistingIsDeleted = existing?.IsDeleted ?? false
            });
        }

        if (duplicateIps.Count > 0)
        {
            foreach (var row in rows.Where(row => duplicateIps.Contains(row.IpAddress)))
            {
                row.Status = CsvImportRowStatus.Duplicate;
            }
        }

        AddSyncDeleteRows(rows, existingDevices, seenCsvIps, options);
        return new CsvImportPreview(totalRows, rows);
    }

    private static CsvImportRowStatus DetermineRowStatus(DeviceCsvRecord record, Device? existing, CsvImportOptions options)
    {
        if (existing is null)
        {
            return CsvImportRowStatus.Add;
        }

        if (existing.IsDeleted)
        {
            return CsvImportRowStatus.Restore;
        }

        return options.Mode switch
        {
            CsvImportMode.AddOnly => CsvImportRowStatus.Skip,
            CsvImportMode.Upsert or CsvImportMode.Sync => HasDeviceChanges(record, existing) ? CsvImportRowStatus.Update : CsvImportRowStatus.Unchanged,
            _ => CsvImportRowStatus.Skip
        };
    }

    private static void AddSyncDeleteRows(
        ICollection<CsvImportPreviewRow> rows,
        IEnumerable<Device> existingDevices,
        HashSet<string> csvIps,
        CsvImportOptions options)
    {
        if (options.Mode != CsvImportMode.Sync)
        {
            return;
        }

        var scopedDevices = existingDevices.Where(device => !device.IsDeleted && device.IsActive);
        if (options.Scope == CsvImportScope.SelectedGroup)
        {
            scopedDevices = scopedDevices.Where(device =>
                (options.GroupId.HasValue && device.GroupId == options.GroupId)
                || (!string.IsNullOrWhiteSpace(options.GroupName) && string.Equals(device.GroupName, options.GroupName, StringComparison.OrdinalIgnoreCase)));
        }

        foreach (var device in scopedDevices)
        {
            var normalizedIp = NormalizeIp(device.IpAddress) ?? device.IpAddress.Trim();
            if (csvIps.Contains(normalizedIp))
            {
                continue;
            }

            rows.Add(new CsvImportPreviewRow
            {
                RowNumber = 0,
                Name = device.Name,
                IpAddress = normalizedIp,
                DeviceType = device.DeviceType.ToDisplayName(),
                GroupName = device.GroupName,
                Location = device.Location,
                Description = device.Description,
                AutoCheckEnabled = device.AutoCheckEnabled,
                CheckIntervalSeconds = device.CheckIntervalSeconds,
                RetryIntervalSeconds = device.FailureRetryIntervalSeconds,
                RetryLimit = device.FailureRetryLimit,
                Status = CsvImportRowStatus.Delete,
                ChangeSummary = "CSV'de bulunmadigi icin soft-delete yapilacak.",
                ExistsInDatabase = true,
                ExistingDeviceId = device.Id
            });
        }
    }

    private static CsvImportPreviewRow CreateInvalidRow(
        int rowNumber,
        string name,
        string ipAddress,
        string deviceType,
        string groupName,
        string location,
        string description,
        string autoCheckEnabled,
        string checkIntervalSeconds,
        string retryIntervalSeconds,
        string retryLimit,
        string error)
    {
        return new CsvImportPreviewRow
        {
            RowNumber = rowNumber,
            Name = name,
            IpAddress = ipAddress,
            DeviceType = deviceType,
            GroupName = groupName,
            Location = location,
            Description = description,
            AutoCheckEnabled = bool.TryParse(autoCheckEnabled, out var parsedAutoCheck) && parsedAutoCheck,
            CheckIntervalSeconds = int.TryParse(checkIntervalSeconds, out var parsedCheckInterval) ? parsedCheckInterval : 0,
            RetryIntervalSeconds = int.TryParse(retryIntervalSeconds, out var parsedRetryInterval) ? parsedRetryInterval : 0,
            RetryLimit = int.TryParse(retryLimit, out var parsedRetryLimit) ? parsedRetryLimit : 0,
            Status = CsvImportRowStatus.Invalid,
            ErrorMessage = error
        };
    }

    private static CsvImportPreviewRow CreateDuplicateRow(
        int rowNumber,
        string name,
        string ipAddress,
        DeviceType deviceType,
        string groupName,
        string location,
        string description,
        bool autoCheckEnabled,
        int checkIntervalSeconds,
        int retryIntervalSeconds,
        int retryLimit,
        string error)
    {
        return new CsvImportPreviewRow
        {
            RowNumber = rowNumber,
            Name = name,
            IpAddress = ipAddress,
            DeviceType = deviceType.ToDisplayName(),
            GroupName = groupName,
            Location = location,
            Description = description,
            AutoCheckEnabled = autoCheckEnabled,
            CheckIntervalSeconds = checkIntervalSeconds,
            RetryIntervalSeconds = retryIntervalSeconds,
            RetryLimit = retryLimit,
            Status = CsvImportRowStatus.Duplicate,
            ErrorMessage = error
        };
    }

    private static string? ValidateImportRow(
        string name,
        string ipAddress,
        string deviceTypeText,
        string isCriticalText,
        string isEnabledText,
        string autoCheckText,
        string pingTimeoutText,
        string checkIntervalText,
        string retryIntervalText,
        string retryLimitText,
        string failureThresholdText,
        out string normalizedIp,
        out DeviceType deviceType,
        out bool isCritical,
        out bool isEnabled,
        out bool autoCheckEnabled,
        out int? pingTimeoutMs,
        out int checkIntervalSeconds,
        out int retryIntervalSeconds,
        out int retryLimit,
        out int failureThreshold)
    {
        normalizedIp = string.Empty;
        deviceType = DeviceType.Other;
        isCritical = false;
        isEnabled = true;
        autoCheckEnabled = true;
        pingTimeoutMs = null;
        checkIntervalSeconds = 0;
        retryIntervalSeconds = 0;
        retryLimit = 0;
        failureThreshold = 0;

        if (string.IsNullOrWhiteSpace(name))
        {
            return "Cihaz adi bos.";
        }

        normalizedIp = NormalizeIp(ipAddress) ?? string.Empty;
        if (string.IsNullOrWhiteSpace(normalizedIp))
        {
            return "Gecersiz IPv4 adresi.";
        }

        if (!DeviceTypeExtensions.TryParse(deviceTypeText, out deviceType))
        {
            return "Cihaz tipi gecersiz.";
        }

        if (!TryParseBoolean(isCriticalText, defaultValue: false, out isCritical))
        {
            return "IsCritical alani true/false, evet/hayir veya 1/0 olmalidir.";
        }

        if (!TryParseBoolean(isEnabledText, defaultValue: true, out isEnabled))
        {
            return "IsEnabled alani true/false, evet/hayir veya 1/0 olmalidir.";
        }

        if (!TryParseBoolean(autoCheckText, defaultValue: true, out autoCheckEnabled))
        {
            return "AutoCheckEnabled alani true/false, evet/hayir veya 1/0 olmalidir.";
        }

        if (!TryParseNullableInt(pingTimeoutText, out pingTimeoutMs)
            || (pingTimeoutMs.HasValue && (pingTimeoutMs.Value < AppSettings.MinPingTimeoutMs || pingTimeoutMs.Value > AppSettings.MaxPingTimeoutMs)))
        {
            return $"PingTimeoutMs {AppSettings.MinPingTimeoutMs}-{AppSettings.MaxPingTimeoutMs} araliginda olmalidir.";
        }

        if (!TryParseOptionalInt(checkIntervalText, out checkIntervalSeconds)
            || checkIntervalSeconds < 0
            || (checkIntervalSeconds > 0 && (checkIntervalSeconds < AppSettings.MinDeviceCheckIntervalSeconds || checkIntervalSeconds > AppSettings.MaxDeviceCheckIntervalSeconds)))
        {
            return $"CheckIntervalSeconds 0 veya {AppSettings.MinDeviceCheckIntervalSeconds}-{AppSettings.MaxDeviceCheckIntervalSeconds} araliginda olmalidir.";
        }

        if (!TryParseOptionalInt(retryIntervalText, out retryIntervalSeconds)
            || retryIntervalSeconds < 0
            || (retryIntervalSeconds > 0 && (retryIntervalSeconds < AppSettings.MinFailureRetryIntervalSeconds || retryIntervalSeconds > AppSettings.MaxFailureRetryIntervalSeconds)))
        {
            return $"RetryIntervalSeconds 0 veya {AppSettings.MinFailureRetryIntervalSeconds}-{AppSettings.MaxFailureRetryIntervalSeconds} araliginda olmalidir.";
        }

        if (!TryParseOptionalInt(retryLimitText, out retryLimit)
            || retryLimit < 0
            || (retryLimit > 0 && (retryLimit < AppSettings.MinFailureRetryLimit || retryLimit > AppSettings.MaxFailureRetryLimit)))
        {
            return $"RetryLimit 0 veya {AppSettings.MinFailureRetryLimit}-{AppSettings.MaxFailureRetryLimit} araliginda olmalidir.";
        }

        if (!TryParseOptionalInt(failureThresholdText, out failureThreshold)
            || failureThreshold < 0
            || (failureThreshold > 0 && (failureThreshold < AppSettings.MinFailureThreshold || failureThreshold > AppSettings.MaxFailureThreshold)))
        {
            return $"FailureThreshold 0 veya {AppSettings.MinFailureThreshold}-{AppSettings.MaxFailureThreshold} araliginda olmalidir.";
        }

        return null;
    }

    private static bool HasDeviceChanges(DeviceCsvRecord record, Device existing)
    {
        return !string.Equals(existing.Name, record.Name, StringComparison.Ordinal)
            || !string.Equals(existing.IpAddress, record.IpAddress, StringComparison.OrdinalIgnoreCase)
            || existing.DeviceType != record.DeviceType
            || !string.Equals(existing.GroupName, record.GroupName, StringComparison.Ordinal)
            || !string.Equals(existing.Location, record.Location, StringComparison.Ordinal)
            || !string.Equals(existing.Description, record.Description, StringComparison.Ordinal)
            || existing.IsCritical != record.IsCritical
            || existing.IsEnabled != record.IsEnabled
            || existing.AutoCheckEnabled != record.AutoCheckEnabled
            || existing.PingTimeoutMs != record.PingTimeoutMs
            || existing.CheckIntervalSeconds != record.CheckIntervalSeconds
            || existing.FailureRetryIntervalSeconds != record.RetryIntervalSeconds
            || existing.FailureRetryLimit != record.RetryLimit
            || existing.FailureThreshold != record.FailureThreshold;
    }

    private static string DescribeChanges(DeviceCsvRecord record, Device existing)
    {
        var changes = new List<string>();
        AddChange(changes, "Ad", existing.Name, record.Name);
        AddChange(changes, "Tip", existing.DeviceType.ToDisplayName(), record.DeviceType.ToDisplayName());
        AddChange(changes, "Grup", existing.GroupName, record.GroupName);
        AddChange(changes, "Konum", existing.Location, record.Location);
        AddChange(changes, "Aciklama", existing.Description, record.Description);
        AddChange(changes, "Kritik", existing.IsCritical, record.IsCritical);
        AddChange(changes, "Aktif", existing.IsEnabled, record.IsEnabled);
        AddChange(changes, "Oto", existing.AutoCheckEnabled, record.AutoCheckEnabled);
        AddChange(changes, "Timeout", existing.PingTimeoutMs, record.PingTimeoutMs);
        AddChange(changes, "Aralik", existing.CheckIntervalSeconds, record.CheckIntervalSeconds);
        AddChange(changes, "Retry", existing.FailureRetryIntervalSeconds, record.RetryIntervalSeconds);
        AddChange(changes, "Limit", existing.FailureRetryLimit, record.RetryLimit);
        AddChange(changes, "Esik", existing.FailureThreshold, record.FailureThreshold);
        return changes.Count == 0 ? "Degisiklik yok." : string.Join("; ", changes);
    }

    private static void AddChange<T>(ICollection<string> changes, string label, T oldValue, T newValue)
    {
        if (EqualityComparer<T>.Default.Equals(oldValue, newValue))
        {
            return;
        }

        changes.Add($"{label}: {oldValue} -> {newValue}");
    }

    private static string? NormalizeIp(string value)
    {
        var trimmed = value.Trim();
        var parts = trimmed.Split('.');
        if (parts.Length != 4 || !IPAddress.TryParse(trimmed, out var address) || address.AddressFamily != AddressFamily.InterNetwork)
        {
            return null;
        }

        var normalized = new byte[4];
        for (var index = 0; index < parts.Length; index++)
        {
            if (!byte.TryParse(parts[index], NumberStyles.Integer, CultureInfo.InvariantCulture, out normalized[index]))
            {
                return null;
            }
        }

        return string.Join('.', normalized);
    }

    private static bool TryParseNullableInt(string value, out int? result)
    {
        result = null;
        if (string.IsNullOrWhiteSpace(value))
        {
            return true;
        }

        if (!int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed)
            && !int.TryParse(value, NumberStyles.Integer, CultureInfo.CurrentCulture, out parsed))
        {
            return false;
        }

        result = parsed <= 0 ? null : parsed;
        return true;
    }

    private static bool TryParseOptionalInt(string value, out int result)
    {
        result = 0;
        return string.IsNullOrWhiteSpace(value)
            || int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out result)
            || int.TryParse(value, NumberStyles.Integer, CultureInfo.CurrentCulture, out result);
    }

    private static bool TryParseBoolean(string value, bool defaultValue, out bool result)
    {
        result = defaultValue;
        if (string.IsNullOrWhiteSpace(value))
        {
            return true;
        }

        return value.Trim().ToLowerInvariant() switch
        {
            "true" or "1" or "evet" or "yes" or "aktif" or "acik" or "açik" or "açık" => Set(true, out result),
            "false" or "0" or "hayir" or "hayır" or "no" or "pasif" or "kapali" or "kapalı" => Set(false, out result),
            _ => false
        };
    }

    private static bool Set(bool value, out bool result)
    {
        result = value;
        return true;
    }

    private static Dictionary<string, int> BuildHeaderIndex(IReadOnlyList<string> headers)
    {
        var indexes = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        for (var index = 0; index < headers.Count; index++)
        {
            var canonical = CanonicalColumnName(headers[index]);
            if (!string.IsNullOrWhiteSpace(canonical))
            {
                indexes.TryAdd(canonical, index);
            }
        }

        return indexes;
    }

    private static string CanonicalColumnName(string header)
    {
        return NormalizeHeader(header) switch
        {
            "name" or "cihazadi" or "device name" or "devicename" => "Name",
            "ipaddress" or "ipadresi" or "ip" or "ipadres" => "IpAddress",
            "devicetype" or "cihazturu" or "cihaztipi" or "tip" => "DeviceType",
            "groupname" or "grup" or "group" => "GroupName",
            "location" or "konum" => "Location",
            "description" or "aciklama" or "note" or "not" => "Description",
            "iscritical" or "kritik" => "IsCritical",
            "isenabled" or "aktif" or "etkin" => "IsEnabled",
            "autocheckenabled" or "otomatik kontrol" or "otomatikcontrol" or "otokontrol" => "AutoCheckEnabled",
            "pingtimeoutms" or "ping timeout" or "pingtimeout" or "timeout" => "PingTimeoutMs",
            "checkintervalseconds" or "kontrolaraligi" or "kontrol araligi" => "CheckIntervalSeconds",
            "retryintervalseconds" or "failureretryintervalseconds" or "retryaraligi" => "RetryIntervalSeconds",
            "retrylimit" or "failureretrylimit" or "retrylimiti" => "RetryLimit",
            "failurethreshold" or "basarisizlikesigi" or "esik" => "FailureThreshold",
            _ => string.Empty
        };
    }

    private static string NormalizeHeader(string value)
    {
        return value
            .Trim()
            .ToLowerInvariant()
            .Replace("ı", "i")
            .Replace("ğ", "g")
            .Replace("ü", "u")
            .Replace("ş", "s")
            .Replace("ö", "o")
            .Replace("ç", "c")
            .Replace("_", string.Empty)
            .Replace("-", string.Empty)
            .Replace(".", string.Empty);
    }

    private static string GetValue(IReadOnlyList<string> values, IReadOnlyDictionary<string, int> indexes, string column)
    {
        if (!indexes.TryGetValue(column, out var index))
        {
            return string.Empty;
        }

        return index >= 0 && index < values.Count ? values[index] : string.Empty;
    }

    private static List<string> ParseRow(string row, char separator)
    {
        var values = new List<string>();
        var builder = new StringBuilder();
        var inQuotes = false;

        for (var index = 0; index < row.Length; index++)
        {
            var character = row[index];
            if (character == '"')
            {
                if (inQuotes && index + 1 < row.Length && row[index + 1] == '"')
                {
                    builder.Append('"');
                    index++;
                }
                else
                {
                    inQuotes = !inQuotes;
                }

                continue;
            }

            if (character == separator && !inQuotes)
            {
                values.Add(builder.ToString());
                builder.Clear();
                continue;
            }

            builder.Append(character);
        }

        values.Add(builder.ToString());
        return values;
    }

    private static void AppendRow(StringBuilder builder, char separator, params string?[] values)
    {
        builder.AppendLine(string.Join(separator, values.Select(value => Escape(value, separator))));
    }

    private static string Escape(string? value, char separator)
    {
        var safeValue = SanitizeSpreadsheetFormula(value ?? string.Empty);
        if (safeValue.Contains(separator) || safeValue.Contains('"') || safeValue.Contains('\r') || safeValue.Contains('\n'))
        {
            return $"\"{safeValue.Replace("\"", "\"\"")}\"";
        }

        return safeValue;
    }

    private static string SanitizeSpreadsheetFormula(string value)
    {
        var firstContentIndex = 0;
        while (firstContentIndex < value.Length && char.IsWhiteSpace(value[firstContentIndex]))
        {
            firstContentIndex++;
        }

        if (firstContentIndex >= value.Length)
        {
            return value;
        }

        return value[firstContentIndex] is '=' or '+' or '-' or '@'
            ? value.Insert(firstContentIndex, "'")
            : value;
    }

    private static string FormatPercent(double? value)
    {
        return value.HasValue
            ? value.Value.ToString("0.00", CultureInfo.CurrentCulture)
            : string.Empty;
    }

    private static string FormatInvariantPercent(double? value)
    {
        return value.HasValue
            ? value.Value.ToString("0.000", CultureInfo.InvariantCulture)
            : string.Empty;
    }

    private static string FormatUtc(DateTime value)
    {
        return value.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture);
    }

    private static string FormatNullableUtc(DateTime? value)
    {
        return value.HasValue ? FormatUtc(value.Value) : string.Empty;
    }

    private static char NormalizeDelimiter(string? delimiter)
    {
        return string.IsNullOrEmpty(delimiter) ? AppSettings.DefaultCsvDelimiter[0] : delimiter[0];
    }
}
