using System.IO;
using System.Globalization;
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
        AppendRow(builder, separator, "Name", "IpAddress", "DeviceType", "GroupName", "Location", "Description", "AutoCheckEnabled", "CheckIntervalSeconds", "RetryIntervalSeconds", "RetryLimit");

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
                device.AutoCheckEnabled.ToString().ToLowerInvariant(),
                device.CheckIntervalSeconds.ToString(CultureInfo.InvariantCulture),
                device.FailureRetryIntervalSeconds.ToString(CultureInfo.InvariantCulture),
                device.FailureRetryLimit.ToString(CultureInfo.InvariantCulture));
        }

        await File.WriteAllTextAsync(filePath, builder.ToString(), Utf8Bom);
    }

    public async Task ExportDeviceTemplateAsync(string filePath, string delimiter = AppSettings.DefaultCsvDelimiter)
    {
        var separator = NormalizeDelimiter(delimiter);
        var builder = new StringBuilder();
        AppendRow(builder, separator, "Name", "IpAddress", "DeviceType", "GroupName", "Location", "Description", "AutoCheckEnabled", "CheckIntervalSeconds", "RetryIntervalSeconds", "RetryLimit");
        AppendRow(builder, separator, "Kamera 1", "192.168.1.10", "Kamera", "Kameralar", "Depo", "Örnek kamera", "true", "0", "0", "0");
        AppendRow(
            builder,
            separator,
            "Switch Ana",
            "192.168.1.2",
            "Switch",
            "Switchler",
            "Sistem Odası",
            "Örnek switch",
            "true",
            (AppSettings.DefaultAutoCheckIntervalMinutes * 60).ToString(CultureInfo.InvariantCulture),
            AppSettings.DefaultFailureRetryIntervalSecondsValue.ToString(CultureInfo.InvariantCulture),
            AppSettings.DefaultFailureRetryLimitValue.ToString(CultureInfo.InvariantCulture));
        await File.WriteAllTextAsync(filePath, builder.ToString(), Utf8Bom);
    }

    public async Task ExportLogsAsync(IEnumerable<PingLog> logs, string filePath, string delimiter = AppSettings.DefaultCsvDelimiter)
    {
        var separator = NormalizeDelimiter(delimiter);
        var builder = new StringBuilder();
        AppendRow(
            builder,
            separator,
            "Tarih/Saat",
            "Cihaz adı",
            "IP adresi",
            "Cihaz tipi",
            "Durum",
            "Gecikme",
            "Hata mesajı");

        foreach (var log in logs)
        {
            AppendRow(
                builder,
                separator,
                log.CheckedAtText,
                log.DeviceName,
                log.IpAddress,
                log.DeviceTypeText,
                log.StatusText,
                log.LatencyText,
                log.ErrorMessage);
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
            "Cihaz adı",
            "IP adresi",
            "Cihaz tipi",
            "Grup",
            "Son durum",
            "Son başarılı kontrol",
            "Son başarısız kontrol",
            "Başarılı kontrol",
            "Başarısız kontrol",
            "Ölçülen erişilebilirlik",
            "Son 24 saat",
            "Son 7 gün",
            "Son 30 gün",
            "Kesinti sayısı",
            "Son kesinti başlangıcı",
            "Son toparlanma",
            "Tahmini kesinti süresi");

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
                item.TotalSuccessCount.ToString(),
                item.TotalFailureCount.ToString(),
                item.MeasuredAvailabilityText,
                item.Availability24HoursText,
                item.Availability7DaysText,
                item.Availability30DaysText,
                item.OutageCount.ToString(),
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
                error.RowNumber.ToString(),
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
        var separator = NormalizeDelimiter(delimiter);
        var lines = await File.ReadAllLinesAsync(filePath, Encoding.UTF8);
        var rows = new List<CsvImportPreviewRow>();
        var existingIps = existingDevices
            .Select(device => device.IpAddress)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        if (lines.Length == 0)
        {
            rows.Add(CreateInvalidRow(1, string.Empty, string.Empty, string.Empty, string.Empty, string.Empty, string.Empty, string.Empty, "CSV dosyası boş."));
            return new CsvImportPreview(0, rows);
        }

        var headers = ParseRow(lines[0], separator);
        var indexes = headers
            .Select((header, index) => new { Header = header.Trim(), Index = index })
            .GroupBy(item => item.Header, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.First().Index, StringComparer.OrdinalIgnoreCase);

        var missingColumns = new[] { "Name", "IpAddress", "DeviceType" }
            .Where(column => !indexes.ContainsKey(column))
            .ToList();

        if (missingColumns.Count > 0)
        {
            rows.Add(CreateInvalidRow(
                1,
                string.Empty,
                string.Empty,
                string.Empty,
                string.Empty,
                string.Empty,
                string.Empty,
                string.Empty,
                $"Zorunlu kolon eksik: {string.Join(", ", missingColumns)}"));
            return new CsvImportPreview(Math.Max(0, lines.Length - 1), rows);
        }

        var seenCsvIps = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
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
            var ipAddress = GetValue(values, indexes, "IpAddress").Trim();
            var deviceTypeText = GetValue(values, indexes, "DeviceType").Trim();
            var groupName = indexes.ContainsKey("GroupName") ? GetValue(values, indexes, "GroupName").Trim() : string.Empty;
            var location = indexes.ContainsKey("Location") ? GetValue(values, indexes, "Location").Trim() : string.Empty;
            var description = indexes.ContainsKey("Description")
                ? GetValue(values, indexes, "Description").Trim()
                : indexes.ContainsKey("Note") ? GetValue(values, indexes, "Note").Trim() : string.Empty;
            var autoCheckText = indexes.ContainsKey("AutoCheckEnabled") ? GetValue(values, indexes, "AutoCheckEnabled").Trim() : "true";
            var checkIntervalText = indexes.ContainsKey("CheckIntervalSeconds") ? GetValue(values, indexes, "CheckIntervalSeconds").Trim() : "0";
            var retryIntervalText = indexes.ContainsKey("RetryIntervalSeconds") ? GetValue(values, indexes, "RetryIntervalSeconds").Trim() : "0";
            var retryLimitText = indexes.ContainsKey("RetryLimit") ? GetValue(values, indexes, "RetryLimit").Trim() : "0";

            var error = ValidateImportRow(
                name,
                ipAddress,
                deviceTypeText,
                autoCheckText,
                checkIntervalText,
                retryIntervalText,
                retryLimitText,
                seenCsvIps,
                out var deviceType,
                out var autoCheckEnabled,
                out var checkIntervalSeconds,
                out var retryIntervalSeconds,
                out var retryLimit);

            if (error is not null)
            {
                rows.Add(CreateInvalidRow(rowNumber, name, ipAddress, deviceTypeText, groupName, location, description, autoCheckText, checkIntervalText, retryIntervalText, retryLimitText, error));
                continue;
            }

            seenCsvIps.Add(ipAddress);
            var record = new DeviceCsvRecord(rowNumber, name, ipAddress, deviceType, groupName, location, description, autoCheckEnabled, checkIntervalSeconds, retryIntervalSeconds, retryLimit);
            var exists = existingIps.Contains(ipAddress);
            rows.Add(new CsvImportPreviewRow
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
                Status = exists ? CsvImportRowStatus.Duplicate : CsvImportRowStatus.Add,
                Record = record,
                ExistsInDatabase = exists
            });
        }

        return new CsvImportPreview(totalRows, rows);
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

    private static string? ValidateImportRow(
        string name,
        string ipAddress,
        string deviceTypeText,
        string autoCheckText,
        string checkIntervalText,
        string retryIntervalText,
        string retryLimitText,
        HashSet<string> seenCsvIps,
        out DeviceType deviceType,
        out bool autoCheckEnabled,
        out int checkIntervalSeconds,
        out int retryIntervalSeconds,
        out int retryLimit)
    {
        deviceType = DeviceType.Other;
        autoCheckEnabled = true;
        checkIntervalSeconds = 0;
        retryIntervalSeconds = 0;
        retryLimit = 0;

        if (string.IsNullOrWhiteSpace(name))
        {
            return "Cihaz adı boş.";
        }

        if (string.IsNullOrWhiteSpace(ipAddress))
        {
            return "IP boş.";
        }

        if (!IpAddressValidator.IsValidIpv4(ipAddress))
        {
            return "Geçersiz IPv4 adresi.";
        }

        if (seenCsvIps.Contains(ipAddress))
        {
            return "Aynı CSV içinde duplicate IP var.";
        }

        if (!DeviceTypeExtensions.TryParse(deviceTypeText, out deviceType))
        {
            return "Cihaz tipi geçersiz.";
        }

        if (!TryParseBoolean(autoCheckText, out autoCheckEnabled))
        {
            return "AutoCheckEnabled alanı true/false, evet/hayır veya 1/0 olmalıdır.";
        }

        if (!TryParseOptionalInt(checkIntervalText, out checkIntervalSeconds)
            || checkIntervalSeconds < 0
            || (checkIntervalSeconds > 0
                && (checkIntervalSeconds < AppSettings.MinDeviceCheckIntervalSeconds
                    || checkIntervalSeconds > AppSettings.MaxDeviceCheckIntervalSeconds)))
        {
            return $"CheckIntervalSeconds 0 veya {AppSettings.MinDeviceCheckIntervalSeconds}-{AppSettings.MaxDeviceCheckIntervalSeconds} aralığında olmalıdır.";
        }

        if (!TryParseOptionalInt(retryIntervalText, out retryIntervalSeconds)
            || retryIntervalSeconds < 0
            || (retryIntervalSeconds > 0
                && (retryIntervalSeconds < AppSettings.MinFailureRetryIntervalSeconds
                    || retryIntervalSeconds > AppSettings.MaxFailureRetryIntervalSeconds)))
        {
            return $"RetryIntervalSeconds 0 veya {AppSettings.MinFailureRetryIntervalSeconds}-{AppSettings.MaxFailureRetryIntervalSeconds} aralığında olmalıdır.";
        }

        if (!TryParseOptionalInt(retryLimitText, out retryLimit)
            || retryLimit < 0
            || (retryLimit > 0
                && (retryLimit < AppSettings.MinFailureRetryLimit
                    || retryLimit > AppSettings.MaxFailureRetryLimit)))
        {
            return $"RetryLimit 0 veya {AppSettings.MinFailureRetryLimit}-{AppSettings.MaxFailureRetryLimit} aralığında olmalıdır.";
        }

        return null;
    }

    private static bool TryParseOptionalInt(string value, out int result)
    {
        result = 0;
        return string.IsNullOrWhiteSpace(value)
            || int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out result)
            || int.TryParse(value, NumberStyles.Integer, CultureInfo.CurrentCulture, out result);
    }

    private static bool TryParseBoolean(string value, out bool result)
    {
        result = false;
        if (string.IsNullOrWhiteSpace(value))
        {
            return true;
        }

        return value.Trim().ToLowerInvariant() switch
        {
            "true" or "1" or "evet" or "yes" => Set(true, out result),
            "false" or "0" or "hayır" or "hayir" or "no" => Set(false, out result),
            _ => false
        };
    }

    private static bool Set(bool value, out bool result)
    {
        result = value;
        return true;
    }

    private static string GetValue(IReadOnlyList<string> values, IReadOnlyDictionary<string, int> indexes, string column)
    {
        var index = indexes[column];
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
        var safeValue = value ?? string.Empty;
        if (safeValue.Contains(separator) || safeValue.Contains('"') || safeValue.Contains('\r') || safeValue.Contains('\n'))
        {
            return $"\"{safeValue.Replace("\"", "\"\"")}\"";
        }

        return safeValue;
    }

    private static string FormatPercent(double? value)
    {
        return value.HasValue
            ? value.Value.ToString("0.00", CultureInfo.CurrentCulture)
            : string.Empty;
    }

    private static char NormalizeDelimiter(string? delimiter)
    {
        return string.IsNullOrEmpty(delimiter) ? AppSettings.DefaultCsvDelimiter[0] : delimiter[0];
    }
}
