namespace NetworkHealthMonitor.Services;

public sealed record WindowsServiceStatus(string Code, string DisplayText)
{
    public static WindowsServiceStatus Unknown(string displayText)
    {
        return new WindowsServiceStatus("Unknown", displayText);
    }
}
