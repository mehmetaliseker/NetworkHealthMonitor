using NetworkHealthMonitor.Models;
using NetworkHealthMonitor.ViewModels.Navigation;
using NetworkHealthMonitor.ViewModels.Pages;
using NetworkHealthMonitor.ViewModels.Shell;

namespace NetworkHealthMonitor.ViewModels.Devices;

public sealed class DeviceDetailsViewModel : LegacyPageViewModelBase
{
    public DeviceDetailsViewModel(MainViewModel legacy, ShellViewModel shell)
        : base(legacy, shell, "Cihaz Detayı", "Tek cihaza ait durum, geçmiş, kesinti ve bildirim bilgileri.")
    {
    }

    public override string BreadcrumbText => Legacy.SelectedDevice is null
        ? "Cihazlar > Cihaz Detayi"
        : $"Cihazlar > {Legacy.SelectedDevice.Name}";

    protected override Task OnBeforeNavigatedToAsync(NavigationContext context, CancellationToken cancellationToken)
    {
        if (context.Parameter is Device device)
        {
            Legacy.OpenDeviceDetailsCommand.Execute(device);
        }

        return Task.CompletedTask;
    }

    protected override bool ShouldReloadOnNavigate(NavigationContext context)
    {
        return context.Parameter is not null;
    }

    protected override bool ResolveHasData()
    {
        return Legacy.SelectedDevice is not null;
    }
}
