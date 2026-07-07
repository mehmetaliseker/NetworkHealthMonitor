using NetworkHealthMonitor.Infrastructure;

namespace NetworkHealthMonitor.ViewModels;

public sealed class SummaryCardViewModel : ObservableObject
{
    private string _value;

    public SummaryCardViewModel(string title, string value, string accentColor)
    {
        Title = title;
        _value = value;
        AccentColor = accentColor;
    }

    public string Title { get; }

    public string Value
    {
        get => _value;
        set => SetProperty(ref _value, value);
    }

    public string AccentColor { get; }
}
