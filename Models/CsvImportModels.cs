namespace NetworkHealthMonitor.Models;

public enum CsvImportDuplicateAction
{
    SkipExisting,
    UpdateExisting,
    Cancel
}

public enum CsvImportRowStatus
{
    Add,
    Update,
    Skip,
    Invalid,
    Duplicate
}

public sealed record DeviceCsvRecord(
    int RowNumber,
    string Name,
    string IpAddress,
    DeviceType DeviceType,
    string Location,
    string GroupName,
    bool IsCritical,
    string Note);

public sealed record CsvImportError(
    int RowNumber,
    string Name,
    string IpAddress,
    string DeviceType,
    string Location,
    string GroupName,
    string IsCritical,
    string Note,
    string Error);

public sealed class CsvImportPreviewRow
{
    public int RowNumber { get; init; }

    public string Name { get; init; } = string.Empty;

    public string IpAddress { get; init; } = string.Empty;

    public string DeviceType { get; init; } = string.Empty;

    public string Location { get; init; } = string.Empty;

    public string GroupName { get; init; } = string.Empty;

    public bool IsCritical { get; init; }

    public string Note { get; init; } = string.Empty;

    public CsvImportRowStatus Status { get; set; }

    public string ErrorMessage { get; init; } = string.Empty;

    public DeviceCsvRecord? Record { get; init; }

    public bool ExistsInDatabase { get; init; }

    public bool IsImportable => Record is not null && Status is CsvImportRowStatus.Add or CsvImportRowStatus.Update or CsvImportRowStatus.Skip or CsvImportRowStatus.Duplicate;

    public string StatusText => Status switch
    {
        CsvImportRowStatus.Add => "Eklenecek",
        CsvImportRowStatus.Update => "Güncellenecek",
        CsvImportRowStatus.Skip => "Atlanacak",
        CsvImportRowStatus.Invalid => "Hatalı",
        CsvImportRowStatus.Duplicate => "Duplicate",
        _ => "Hatalı"
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

    public IReadOnlyList<DeviceCsvRecord> ValidRows => Rows
        .Where(row => row.Record is not null && row.Status != CsvImportRowStatus.Invalid)
        .Select(row => row.Record!)
        .ToList();

    public IReadOnlyList<CsvImportError> Errors => Rows
        .Where(row => row.Status == CsvImportRowStatus.Invalid)
        .Select(row => new CsvImportError(
            row.RowNumber,
            row.Name,
            row.IpAddress,
            row.DeviceType,
            row.Location,
            row.GroupName,
            row.IsCritical.ToString().ToLowerInvariant(),
            row.Note,
            row.ErrorMessage))
        .ToList();

    public int ExistingIpCount => Rows.Count(row => row.ExistsInDatabase && row.Status != CsvImportRowStatus.Invalid);

    public int AddCount => Rows.Count(row => row.Status == CsvImportRowStatus.Add);

    public int UpdateCount => Rows.Count(row => row.Status == CsvImportRowStatus.Update);

    public int SkipCount => Rows.Count(row => row.Status == CsvImportRowStatus.Skip);

    public int DuplicateCount => Rows.Count(row => row.Status == CsvImportRowStatus.Duplicate);

    public int InvalidRowCount => Rows.Count(row => row.Status == CsvImportRowStatus.Invalid);

    public bool HasImportableRows => Rows.Any(row => row.Record is not null && row.Status != CsvImportRowStatus.Invalid);
}

public sealed record CsvImportApplyResult(
    int Added,
    int Updated,
    int Skipped,
    int Invalid);
