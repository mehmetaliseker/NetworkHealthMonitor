namespace NetworkHealthMonitor.Models;

public enum CsvImportDuplicateAction
{
    SkipExisting,
    UpdateExisting,
    Cancel
}

public enum CsvImportMode
{
    AddOnly,
    Upsert,
    Sync
}

public enum CsvImportScope
{
    AllActiveDevices,
    SelectedGroup
}

public enum CsvImportRowStatus
{
    Add,
    Update,
    Restore,
    Unchanged,
    Delete,
    Skip,
    Invalid,
    Duplicate
}

public sealed record DeviceCsvRecord(
    int RowNumber,
    string Name,
    string IpAddress,
    DeviceType DeviceType,
    string GroupName,
    string Location,
    string Description,
    bool IsCritical,
    bool IsEnabled,
    bool AutoCheckEnabled,
    int? PingTimeoutMs,
    int CheckIntervalSeconds,
    int RetryIntervalSeconds,
    int RetryLimit,
    int FailureThreshold);

public sealed record CsvImportOptions(
    CsvImportMode Mode,
    CsvImportScope Scope,
    int? GroupId,
    string GroupName,
    string FileName,
    string InitiatedBy);

public sealed record CsvImportError(
    int RowNumber,
    string Name,
    string IpAddress,
    string DeviceType,
    string GroupName,
    string Location,
    string Description,
    string AutoCheckEnabled,
    string CheckIntervalSeconds,
    string RetryIntervalSeconds,
    string RetryLimit,
    string Error);

public sealed class CsvImportPreviewRow
{
    public int RowNumber { get; init; }

    public string Name { get; init; } = string.Empty;

    public string IpAddress { get; init; } = string.Empty;

    public string DeviceType { get; init; } = string.Empty;

    public string GroupName { get; init; } = string.Empty;

    public string Location { get; init; } = string.Empty;

    public string Description { get; init; } = string.Empty;

    public bool AutoCheckEnabled { get; init; }

    public int CheckIntervalSeconds { get; init; }

    public int RetryIntervalSeconds { get; init; }

    public int RetryLimit { get; init; }

    public CsvImportRowStatus Status { get; set; }

    public string ErrorMessage { get; init; } = string.Empty;

    public string ChangeSummary { get; init; } = string.Empty;

    public DeviceCsvRecord? Record { get; init; }

    public bool ExistsInDatabase { get; init; }

    public int? ExistingDeviceId { get; init; }

    public bool ExistingIsDeleted { get; init; }

    public bool IsImportable => Status is CsvImportRowStatus.Add
        or CsvImportRowStatus.Update
        or CsvImportRowStatus.Restore
        or CsvImportRowStatus.Delete
        or CsvImportRowStatus.Unchanged
        or CsvImportRowStatus.Skip;

    public string StatusText => Status switch
    {
        CsvImportRowStatus.Add => "Eklenecek",
        CsvImportRowStatus.Update => "Guncellenecek",
        CsvImportRowStatus.Restore => "Geri yuklenecek",
        CsvImportRowStatus.Unchanged => "Degismeyecek",
        CsvImportRowStatus.Delete => "Silinecek",
        CsvImportRowStatus.Skip => "Atlanacak",
        CsvImportRowStatus.Invalid => "Hatali",
        CsvImportRowStatus.Duplicate => "Duplicate",
        _ => "Hatali"
    };
}

public sealed class CsvImportPreview
{
    public CsvImportPreview(int totalRows, IReadOnlyList<CsvImportPreviewRow> rows)
    {
        TotalRows = totalRows;
        Rows = rows;
    }

    public int TotalRows { get; }

    public IReadOnlyList<CsvImportPreviewRow> Rows { get; }

    public IReadOnlyList<DeviceCsvRecord> ApplyRows => Rows
        .Where(row => row.Record is not null
            && row.Status is CsvImportRowStatus.Add
                or CsvImportRowStatus.Update
                or CsvImportRowStatus.Restore
                or CsvImportRowStatus.Unchanged
                or CsvImportRowStatus.Skip)
        .Select(row => row.Record!)
        .ToList();

    public IReadOnlyList<DeviceCsvRecord> ValidRows => ApplyRows;

    public IReadOnlyList<CsvImportError> Errors => Rows
        .Where(row => row.Status == CsvImportRowStatus.Invalid)
        .Select(row => new CsvImportError(
            row.RowNumber,
            row.Name,
            row.IpAddress,
            row.DeviceType,
            row.GroupName,
            row.Location,
            row.Description,
            row.AutoCheckEnabled.ToString().ToLowerInvariant(),
            row.CheckIntervalSeconds.ToString(),
            row.RetryIntervalSeconds.ToString(),
            row.RetryLimit.ToString(),
            row.ErrorMessage))
        .ToList();

    public int ExistingIpCount => Rows.Count(row => row.ExistsInDatabase && row.Status != CsvImportRowStatus.Invalid);

    public int AddCount => Rows.Count(row => row.Status == CsvImportRowStatus.Add);

    public int UpdateCount => Rows.Count(row => row.Status == CsvImportRowStatus.Update);

    public int RestoreCount => Rows.Count(row => row.Status == CsvImportRowStatus.Restore);

    public int DeleteCount => Rows.Count(row => row.Status == CsvImportRowStatus.Delete);

    public int UnchangedCount => Rows.Count(row => row.Status == CsvImportRowStatus.Unchanged);

    public int SkipCount => Rows.Count(row => row.Status == CsvImportRowStatus.Skip);

    public int DuplicateCount => Rows.Count(row => row.Status == CsvImportRowStatus.Duplicate);

    public int InvalidRowCount => Rows.Count(row => row.Status == CsvImportRowStatus.Invalid);

    public int ValidCsvRowCount => Rows.Count(row => row.Record is not null && row.Status != CsvImportRowStatus.Duplicate);

    public bool HasBlockingErrors => InvalidRowCount > 0 || DuplicateCount > 0 || ValidCsvRowCount == 0;

    public bool HasImportableRows => Rows.Any(row => row.IsImportable) && !HasBlockingErrors;

    public string SummaryText => $"""
        CSV satir sayisi: {TotalRows}
        Gecerli satir: {ValidCsvRowCount}
        Hatali satir: {InvalidRowCount}

        Yeni eklenecek: {AddCount}
        Guncellenecek: {UpdateCount}
        Degismeden kalacak: {UnchangedCount}
        CSV'de bulunmadigi icin silinecek: {DeleteCount}
        Geri yuklenecek: {RestoreCount}
        Atlanacak: {SkipCount}
        Duplicate kayit: {DuplicateCount}
        """;
}

public sealed record CsvImportApplyResult(
    int Added,
    int Updated,
    int Deleted,
    int Restored,
    int Unchanged,
    int Skipped,
    int Invalid,
    string BackupPath,
    long AuditId);
