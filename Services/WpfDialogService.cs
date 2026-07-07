using System.Windows;
using Microsoft.Win32;
using NetworkHealthMonitor.Models;

namespace NetworkHealthMonitor.Services;

public sealed class WpfDialogService : IDialogService
{
    public void ShowInfo(string title, string message)
    {
        MessageBox.Show(message, title, MessageBoxButton.OK, MessageBoxImage.Information);
    }

    public void ShowWarning(string title, string message)
    {
        MessageBox.Show(message, title, MessageBoxButton.OK, MessageBoxImage.Warning);
    }

    public void ShowError(string title, string message)
    {
        MessageBox.Show(message, title, MessageBoxButton.OK, MessageBoxImage.Error);
    }

    public bool Confirm(string title, string message)
    {
        return MessageBox.Show(message, title, MessageBoxButton.YesNo, MessageBoxImage.Question) == MessageBoxResult.Yes;
    }

    public CsvImportDuplicateAction ChooseDuplicateImportAction(string title, string message)
    {
        var result = MessageBox.Show(
            $"{message}\n\nEvet: var olanları güncelle\nHayır: var olanları atla\nİptal: import işlemini iptal et",
            title,
            MessageBoxButton.YesNoCancel,
            MessageBoxImage.Question);

        return result switch
        {
            MessageBoxResult.Yes => CsvImportDuplicateAction.UpdateExisting,
            MessageBoxResult.No => CsvImportDuplicateAction.SkipExisting,
            _ => CsvImportDuplicateAction.Cancel
        };
    }

    public void CopyToClipboard(string text)
    {
        Clipboard.SetText(text);
    }

    public string? GetOpenCsvFilePath()
    {
        return GetOpenFilePath("CSV dosyasını seç", "CSV dosyası (*.csv)|*.csv|Tüm dosyalar (*.*)|*.*", ".csv");
    }

    public string? GetSaveCsvFilePath(string defaultFileName)
    {
        return GetSaveFilePath("CSV dosyasını kaydet", defaultFileName, "CSV dosyası (*.csv)|*.csv|Tüm dosyalar (*.*)|*.*", ".csv");
    }

    public string? GetOpenDatabaseFilePath()
    {
        return GetOpenFilePath("SQLite veritabanı seç", "SQLite veritabanı (*.db)|*.db|Tüm dosyalar (*.*)|*.*", ".db");
    }

    public string? GetSaveDatabaseFilePath(string defaultFileName)
    {
        return GetSaveFilePath("Veritabanını yedekle", defaultFileName, "SQLite veritabanı (*.db)|*.db|Tüm dosyalar (*.*)|*.*", ".db");
    }

    public string? GetOpenJsonFilePath()
    {
        return GetOpenFilePath("Ayar dosyası seç", "JSON dosyası (*.json)|*.json|Tüm dosyalar (*.*)|*.*", ".json");
    }

    public string? GetSaveJsonFilePath(string defaultFileName)
    {
        return GetSaveFilePath("Ayarları kaydet", defaultFileName, "JSON dosyası (*.json)|*.json|Tüm dosyalar (*.*)|*.*", ".json");
    }

    private static string? GetOpenFilePath(string title, string filter, string defaultExtension)
    {
        var dialog = new OpenFileDialog
        {
            Title = title,
            DefaultExt = defaultExtension,
            Filter = filter,
            CheckFileExists = true,
            Multiselect = false
        };

        return dialog.ShowDialog() == true ? dialog.FileName : null;
    }

    private static string? GetSaveFilePath(string title, string defaultFileName, string filter, string defaultExtension)
    {
        var dialog = new SaveFileDialog
        {
            Title = title,
            FileName = defaultFileName,
            DefaultExt = defaultExtension,
            Filter = filter,
            AddExtension = true,
            OverwritePrompt = true
        };

        return dialog.ShowDialog() == true ? dialog.FileName : null;
    }
}
