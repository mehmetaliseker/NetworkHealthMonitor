namespace NetworkHealthMonitor.ViewModels;

public sealed class SelectionOption<T>
{
    public SelectionOption(T value, string label)
    {
        Value = value;
        Label = label;
    }

    public T Value { get; }

    public string Label { get; }
}
