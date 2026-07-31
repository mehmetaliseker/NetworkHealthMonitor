using System.ComponentModel;
using NetworkHealthMonitor.Infrastructure;
using NetworkHealthMonitor.ViewModels.Shell;

namespace NetworkHealthMonitor.ViewModels.Pages;

public abstract class LegacyPageViewModelBase : PageViewModelBase
{
    protected LegacyPageViewModelBase(MainViewModel legacy, ShellViewModel shell, string title, string description)
        : base(title, description)
    {
        Legacy = legacy;
        Shell = shell;
        Legacy.PropertyChanged += LegacyPropertyChanged;
    }

    public MainViewModel Legacy { get; }

    public ShellViewModel Shell { get; }

    protected override Task LoadCoreAsync(CancellationToken cancellationToken)
    {
        UpdateStateFromCachedData();
        return Task.CompletedTask;
    }

    public override async ValueTask DisposeAsync()
    {
        Legacy.PropertyChanged -= LegacyPropertyChanged;
        await base.DisposeAsync();
    }

    private void LegacyPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        UpdateStateFromCachedData();
        OnPropertyChanged(nameof(Legacy));
    }
}
