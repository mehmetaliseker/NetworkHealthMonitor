using NetworkHealthMonitor.Models;

namespace NetworkHealthMonitor.Services;

public interface INotificationTemplateRenderer
{
    string Render(string template, NotificationTemplateContext context);

    IReadOnlyList<string> FindUnknownPlaceholders(string template);
}
