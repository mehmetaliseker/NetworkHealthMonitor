using System.ComponentModel;
using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using NetworkHealthMonitor.Data;
using WpfButton = System.Windows.Controls.Button;
using WpfContextMenu = System.Windows.Controls.ContextMenu;
using WpfDataGrid = System.Windows.Controls.DataGrid;
using WpfDataGridColumn = System.Windows.Controls.DataGridColumn;
using WpfDataGridLength = System.Windows.Controls.DataGridLength;
using WpfDataGridLengthUnitType = System.Windows.Controls.DataGridLengthUnitType;
using WpfMenuItem = System.Windows.Controls.MenuItem;

namespace NetworkHealthMonitor.Infrastructure;

public static class DataGridLayoutBehavior
{
    public static readonly DependencyProperty StorageKeyProperty =
        DependencyProperty.RegisterAttached(
            "StorageKey",
            typeof(string),
            typeof(DataGridLayoutBehavior),
            new PropertyMetadata(null, OnStorageKeyChanged));

    public static readonly DependencyProperty ColumnChooserButtonNameProperty =
        DependencyProperty.RegisterAttached(
            "ColumnChooserButtonName",
            typeof(string),
            typeof(DataGridLayoutBehavior),
            new PropertyMetadata(null));

    private static readonly DependencyProperty IsInitializedProperty =
        DependencyProperty.RegisterAttached(
            "IsInitialized",
            typeof(bool),
            typeof(DataGridLayoutBehavior),
            new PropertyMetadata(false));

    private static readonly JsonSerializerOptions SerializerOptions = new() { WriteIndented = true };

    public static string? GetStorageKey(DependencyObject element)
    {
        return (string?)element.GetValue(StorageKeyProperty);
    }

    public static void SetStorageKey(DependencyObject element, string? value)
    {
        element.SetValue(StorageKeyProperty, value);
    }

    public static string? GetColumnChooserButtonName(DependencyObject element)
    {
        return (string?)element.GetValue(ColumnChooserButtonNameProperty);
    }

    public static void SetColumnChooserButtonName(DependencyObject element, string? value)
    {
        element.SetValue(ColumnChooserButtonNameProperty, value);
    }

    private static bool GetIsInitialized(DependencyObject element)
    {
        return (bool)element.GetValue(IsInitializedProperty);
    }

    private static void SetIsInitialized(DependencyObject element, bool value)
    {
        element.SetValue(IsInitializedProperty, value);
    }

    private static void OnStorageKeyChanged(DependencyObject dependencyObject, DependencyPropertyChangedEventArgs args)
    {
        if (dependencyObject is not WpfDataGrid grid || args.NewValue is not string { Length: > 0 })
        {
            return;
        }

        if (grid.IsLoaded)
        {
            Initialize(grid);
            return;
        }

        grid.Loaded += (_, _) => Initialize(grid);
    }

    private static void Initialize(WpfDataGrid grid)
    {
        if (GetIsInitialized(grid))
        {
            return;
        }

        SetIsInitialized(grid, true);
        RestoreLayout(grid);
        AttachPersistence(grid);
        AttachColumnChooser(grid);
    }

    private static void AttachPersistence(WpfDataGrid grid)
    {
        grid.ColumnReordered += (_, _) => SaveLayout(grid);

        var widthDescriptor = DependencyPropertyDescriptor.FromProperty(WpfDataGridColumn.WidthProperty, typeof(WpfDataGridColumn));
        var visibilityDescriptor = DependencyPropertyDescriptor.FromProperty(WpfDataGridColumn.VisibilityProperty, typeof(WpfDataGridColumn));
        var displayIndexDescriptor = DependencyPropertyDescriptor.FromProperty(WpfDataGridColumn.DisplayIndexProperty, typeof(WpfDataGridColumn));

        foreach (var column in grid.Columns)
        {
            widthDescriptor?.AddValueChanged(column, (_, _) => SaveLayout(grid));
            visibilityDescriptor?.AddValueChanged(column, (_, _) => SaveLayout(grid));
            displayIndexDescriptor?.AddValueChanged(column, (_, _) => SaveLayout(grid));
        }
    }

    private static void AttachColumnChooser(WpfDataGrid grid)
    {
        var buttonName = GetColumnChooserButtonName(grid);
        if (string.IsNullOrWhiteSpace(buttonName))
        {
            return;
        }

        grid.Dispatcher.BeginInvoke(() =>
        {
            var window = Window.GetWindow(grid);
            if (window is null)
            {
                return;
            }

            var button = FindVisualChildByName<WpfButton>(window, buttonName);
            if (button is null)
            {
                return;
            }

            button.ContextMenu = CreateColumnChooserMenu(grid);
            button.Click += (_, _) =>
            {
                if (button.ContextMenu is null)
                {
                    return;
                }

                button.ContextMenu.PlacementTarget = button;
                button.ContextMenu.IsOpen = true;
            };
        });
    }

    private static WpfContextMenu CreateColumnChooserMenu(WpfDataGrid grid)
    {
        var menu = new WpfContextMenu();
        foreach (var column in grid.Columns.Where(column => column.Header is not null))
        {
            var item = new WpfMenuItem
            {
                Header = column.Header?.ToString(),
                IsCheckable = true,
                IsChecked = column.Visibility == Visibility.Visible,
                StaysOpenOnClick = true
            };

            item.Checked += (_, _) =>
            {
                column.Visibility = Visibility.Visible;
                SaveLayout(grid);
            };
            item.Unchecked += (_, _) =>
            {
                column.Visibility = Visibility.Collapsed;
                SaveLayout(grid);
            };
            menu.Items.Add(item);
        }

        return menu;
    }

    private static void RestoreLayout(WpfDataGrid grid)
    {
        var storageKey = GetStorageKey(grid);
        if (string.IsNullOrWhiteSpace(storageKey))
        {
            return;
        }

        var store = LoadStore();
        if (!store.Grids.TryGetValue(storageKey, out var columns))
        {
            return;
        }

        foreach (var column in grid.Columns)
        {
            var key = GetColumnKey(grid, column);
            if (!columns.TryGetValue(key, out var state))
            {
                continue;
            }

            column.Width = CreateWidth(state);
            column.Visibility = state.Visible ? Visibility.Visible : Visibility.Collapsed;
        }

        var orderedColumns = grid.Columns
            .Select(column => new { Column = column, Key = GetColumnKey(grid, column) })
            .Where(item => columns.ContainsKey(item.Key))
            .OrderBy(item => columns[item.Key].DisplayIndex)
            .ToList();

        for (var index = 0; index < orderedColumns.Count; index++)
        {
            try
            {
                orderedColumns[index].Column.DisplayIndex = index;
            }
            catch (ArgumentOutOfRangeException)
            {
                // A stale layout should not prevent the grid from opening.
            }
        }
    }

    private static void SaveLayout(WpfDataGrid grid)
    {
        var storageKey = GetStorageKey(grid);
        if (string.IsNullOrWhiteSpace(storageKey))
        {
            return;
        }

        var store = LoadStore();
        store.Grids[storageKey] = grid.Columns.ToDictionary(
            column => GetColumnKey(grid, column),
            column => new ColumnLayoutState(
                column.Width.Value,
                column.Width.UnitType.ToString(),
                column.DisplayIndex,
                column.Visibility == Visibility.Visible));

        Directory.CreateDirectory(DatabasePaths.ConfigDirectory);
        File.WriteAllText(GetLayoutFilePath(), JsonSerializer.Serialize(store, SerializerOptions));
    }

    private static LayoutStore LoadStore()
    {
        var path = GetLayoutFilePath();
        if (!File.Exists(path))
        {
            return new LayoutStore();
        }

        try
        {
            return JsonSerializer.Deserialize<LayoutStore>(File.ReadAllText(path)) ?? new LayoutStore();
        }
        catch (JsonException)
        {
            return new LayoutStore();
        }
        catch (IOException)
        {
            return new LayoutStore();
        }
        catch (UnauthorizedAccessException)
        {
            return new LayoutStore();
        }
    }

    private static string GetLayoutFilePath()
    {
        return Path.Combine(DatabasePaths.ConfigDirectory, "ui-layout.json");
    }

    private static string GetColumnKey(WpfDataGrid grid, WpfDataGridColumn column)
    {
        return $"{grid.Columns.IndexOf(column)}:{column.Header}";
    }

    private static WpfDataGridLength CreateWidth(ColumnLayoutState state)
    {
        return Enum.TryParse<WpfDataGridLengthUnitType>(state.WidthUnit, out var unitType)
            ? new WpfDataGridLength(state.WidthValue, unitType)
            : new WpfDataGridLength(state.WidthValue);
    }

    private static T? FindVisualChildByName<T>(DependencyObject parent, string name)
        where T : FrameworkElement
    {
        for (var index = 0; index < VisualTreeHelper.GetChildrenCount(parent); index++)
        {
            var child = VisualTreeHelper.GetChild(parent, index);
            if (child is T typedChild && string.Equals(typedChild.Name, name, StringComparison.Ordinal))
            {
                return typedChild;
            }

            var nested = FindVisualChildByName<T>(child, name);
            if (nested is not null)
            {
                return nested;
            }
        }

        return null;
    }

    private sealed class LayoutStore
    {
        public Dictionary<string, Dictionary<string, ColumnLayoutState>> Grids { get; set; } = new();
    }

    private sealed record ColumnLayoutState(double WidthValue, string WidthUnit, int DisplayIndex, bool Visible);
}
