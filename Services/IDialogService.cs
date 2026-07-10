using NetworkHealthMonitor.Models;

namespace NetworkHealthMonitor.Services;

public interface IDialogService
{
    void ShowInfo(string title, string message);

    void ShowWarning(string title, string message);

    void ShowError(string title, string message);

    bool Confirm(string title, string message);

    CsvImportDuplicateAction ChooseDuplicateImportAction(string title, string message);

    void CopyToClipboard(string text);

    string? GetOpenCsvFilePath();

    string? GetSaveCsvFilePath(string defaultFileName, string? initialDirectory = null);

    string? GetOpenDatabaseFilePath();

    string? GetSaveDatabaseFilePath(string defaultFileName);

    string? GetOpenJsonFilePath();

    string? GetSaveJsonFilePath(string defaultFileName);
}
