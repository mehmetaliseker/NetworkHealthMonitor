using System.IO;
using System.Text;
using NetworkHealthMonitor.Models;

namespace NetworkHealthMonitor.Services;

public sealed class CsvExportService
{
    private static readonly UTF8Encoding Utf8Bom = new(encoderShouldEmitUTF8Identifier: true);

    public async Task ExportDevicesAsync(IEnumerable<Device> devices, string filePath, string delimiter = ";")
    {
        var separator = NormalizeDelimiter(delimiter);
        var builder = new StringBuilder();
        AppendRow(builder, separator, "Name", "IpAddress", "DeviceType", "Location", "GroupName", "IsCritical", "Note");

        foreach (var device in devices)
        {
            AppendRow(
                builder,
                separator,
                device.Name,
                device.IpAddress,
                device.DeviceTypeText,
                device.Location,
                device.GroupName,
                device.IsCritical.ToString().ToLowerInvariant(),
                device.Description);
        }

        await File.WriteAllTextAsync(filePath, builder.ToString(), Utf8Bom);
    }

    public async Task ExportDeviceTemplateAsync(string filePath, string delimiter = ";")
    {
        var separator = NormalizeDelimiter(delimiter);
        var builder = new StringBuilder();
        AppendRow(builder, separator, "Name", "IpAddress", "DeviceType", "Location", "GroupName", "IsCritical", "Note");
        AppendRow(builder, separator, "Kamera 1", "192.168.1.10", "Kamera", "Depo", "Kameralar", "true", "Örnek kamera");
        AppendRow(builder, separator, "Switch Ana", "192.168.1.2", "Switch", "Sistem Odası", "Switchler", "true", "Örnek switch");
        await File.WriteAllTextAsync(filePath, builder.ToString(), Utf8Bom);
    }

    public async Task ExportLogsAsync(IEnumerable<PingLog> logs, string filePath, string delimiter = ";")
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

    public async Task ExportImportErrorsAsync(IEnumerable<CsvImportError> errors, string filePath, string delimiter = ";")
    {
        var separator = NormalizeDelimiter(delimiter);
        var builder = new StringBuilder();
        AppendRow(builder, separator, "RowNumber", "Name", "IpAddress", "DeviceType", "Location", "GroupName", "IsCritical", "Note", "Error");

        foreach (var error in errors)
        {
            AppendRow(
                builder,
                separator,
                error.RowNumber.ToString(),
                error.Name,
                error.IpAddress,
                error.DeviceType,
                error.Location,
                error.GroupName,
                error.IsCritical,
                error.Note,
                error.Error);
        }

        await File.WriteAllTextAsync(filePath, builder.ToString(), Utf8Bom);
    }

    public async Task<CsvImportPreview> ReadDeviceImportPreviewAsync(
        string filePath,
        IEnumerable<Device> existingDevices,
        string delimiter = ";")
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
            var location = indexes.ContainsKey("Location") ? GetValue(values, indexes, "Location").Trim() : string.Empty;
            var groupName = indexes.ContainsKey("GroupName") ? GetValue(values, indexes, "GroupName").Trim() : string.Empty;
            var isCriticalText = indexes.ContainsKey("IsCritical") ? GetValue(values, indexes, "IsCritical").Trim() : "false";
            var note = indexes.ContainsKey("Note") ? GetValue(values, indexes, "Note").Trim() : string.Empty;

            var error = ValidateImportRow(
                name,
                ipAddress,
                deviceTypeText,
                isCriticalText,
                seenCsvIps,
                out var deviceType,
                out var isCritical);

            if (error is not null)
            {
                rows.Add(CreateInvalidRow(rowNumber, name, ipAddress, deviceTypeText, location, groupName, isCriticalText, note, error));
                continue;
            }

            seenCsvIps.Add(ipAddress);
            var record = new DeviceCsvRecord(rowNumber, name, ipAddress, deviceType, location, groupName, isCritical, note);
            var exists = existingIps.Contains(ipAddress);
            rows.Add(new CsvImportPreviewRow
            {
                RowNumber = rowNumber,
                Name = name,
                IpAddress = ipAddress,
                DeviceType = deviceType.ToDisplayName(),
                Location = location,
                GroupName = groupName,
                IsCritical = isCritical,
                Note = note,
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
        string location,
        string groupName,
        string isCritical,
        string note,
        string error)
    {
        return new CsvImportPreviewRow
        {
            RowNumber = rowNumber,
            Name = name,
            IpAddress = ipAddress,
            DeviceType = deviceType,
            Location = location,
            GroupName = groupName,
            IsCritical = bool.TryParse(isCritical, out var parsed) && parsed,
            Note = note,
            Status = CsvImportRowStatus.Invalid,
            ErrorMessage = error
        };
    }

    private static string? ValidateImportRow(
        string name,
        string ipAddress,
        string deviceTypeText,
        string isCriticalText,
        HashSet<string> seenCsvIps,
        out DeviceType deviceType,
        out bool isCritical)
    {
        deviceType = DeviceType.Other;
        isCritical = false;

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

        if (!TryParseBoolean(isCriticalText, out isCritical))
        {
            return "Kritik alanı true/false, evet/hayır veya 1/0 olmalıdır.";
        }

        return null;
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

    private static char NormalizeDelimiter(string? delimiter)
    {
        return string.IsNullOrEmpty(delimiter) ? ';' : delimiter[0];
    }
}
