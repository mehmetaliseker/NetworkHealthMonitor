using NetworkHealthMonitor.Infrastructure;

namespace NetworkHealthMonitor.ViewModels;

public sealed class MetricRowViewModel : ObservableObject
{
    private string _title = string.Empty;
    private string _value = string.Empty;
    private double _percent;
    private string _accentColor = "#2563EB";

    public string Title
    {
        get => _title;
        set => SetProperty(ref _title, value ?? string.Empty);
    }

    public string Value
    {
        get => _value;
        set => SetProperty(ref _value, value ?? string.Empty);
    }

    public double Percent
    {
        get => _percent;
        set => SetProperty(ref _percent, Math.Clamp(value, 0, 100));
    }

    public string AccentColor
    {
        get => _accentColor;
        set => SetProperty(ref _accentColor, value ?? "#2563EB");
    }
}
