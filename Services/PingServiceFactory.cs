namespace NetworkHealthMonitor.Services;

public static class PingServiceFactory
{
    public static IPingService Create(AppSettingsService settingsService)
    {
        return new AcceptancePingService(new PingService(), settingsService);
    }
}
