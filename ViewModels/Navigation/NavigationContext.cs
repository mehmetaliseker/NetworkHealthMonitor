namespace NetworkHealthMonitor.ViewModels.Navigation;

public sealed record NavigationContext(
    string RouteName,
    object? Parameter = null);
