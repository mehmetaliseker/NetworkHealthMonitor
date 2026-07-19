using System.Text.RegularExpressions;
using NetworkHealthMonitor.Models;

namespace NetworkHealthMonitor.Services;

public sealed class NotificationTemplateRenderer : INotificationTemplateRenderer
{
    private static readonly Regex PlaceholderPattern = new(@"\{(?<name>[A-Za-z][A-Za-z0-9]*)\}", RegexOptions.Compiled);

    public string Render(string template, NotificationTemplateContext context)
    {
        var unknown = FindUnknownPlaceholders(template);
        if (unknown.Count > 0)
        {
            throw new InvalidOperationException("Unknown notification placeholder(s): " + string.Join(", ", unknown));
        }

        var values = context.ToValues();
        return PlaceholderPattern.Replace(template ?? string.Empty, match =>
        {
            var name = match.Groups["name"].Value;
            return values.TryGetValue(name, out var value) ? value : match.Value;
        });
    }

    public IReadOnlyList<string> FindUnknownPlaceholders(string template)
    {
        var allowed = NotificationTemplatePlaceholders.All.ToHashSet(StringComparer.OrdinalIgnoreCase);
        return PlaceholderPattern
            .Matches(template ?? string.Empty)
            .Select(match => match.Groups["name"].Value)
            .Where(name => !allowed.Contains(name))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(name => name, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }
}
