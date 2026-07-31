using System.Windows.Input;
using NetworkHealthMonitor.Infrastructure;

namespace NetworkHealthMonitor.ViewModels.Shell;

public sealed class NavigationItemViewModel : ObservableObject
{
    public required string Number { get; init; }

    public required string Label { get; init; }

    public required string AutomationName { get; init; }

    public required string ToolTip { get; init; }

    public required ICommand Command { get; init; }

    public required Func<bool> IsSelectedResolver { get; init; }

    public bool IsSelected => IsSelectedResolver();

    public void NotifySelectedChanged()
    {
        OnPropertyChanged(nameof(IsSelected));
    }
}
