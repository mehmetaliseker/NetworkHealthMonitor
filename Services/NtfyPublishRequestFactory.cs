using System.Globalization;
using NetworkHealthMonitor.Models;

namespace NetworkHealthMonitor.Services;

public static class NtfyPublishRequestFactory
{
    public static Uri NormalizeBaseUri(string? baseUrl)
    {
        var trimmed = (baseUrl ?? string.Empty).Trim().TrimEnd('/');
        if (!Uri.TryCreate(trimmed, UriKind.Absolute, out var uri))
        {
            throw new ArgumentException("Notification server URL is invalid.", nameof(baseUrl));
        }

        if (uri.Scheme is not ("http" or "https"))
        {
            throw new ArgumentException("Notification server URL must use http or https.", nameof(baseUrl));
        }

        return uri;
    }

    public static string NormalizeTopic(string? topic)
    {
        return (topic ?? string.Empty).Trim();
    }

    public static bool IsTopicValid(string? topic)
    {
        var normalized = NormalizeTopic(topic);
        if (string.IsNullOrWhiteSpace(normalized))
        {
            return false;
        }

        if (normalized.Contains('/') || normalized.Contains('?') || normalized.Contains('#'))
        {
            return false;
        }

        return true;
    }

    public static int ResolvePriority(string? priority)
    {
        var value = (priority ?? string.Empty).Trim();
        if (int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var numeric))
        {
            return Math.Clamp(numeric, 1, 5);
        }

        return value.ToLowerInvariant() switch
        {
            "max" or "urgent" => 5,
            "high" => 4,
            "default" or "" => 3,
            "low" => 2,
            "min" => 1,
            _ => 3
        };
    }

    public static IReadOnlyList<string>? ResolveTags(string? tags)
    {
        if (string.IsNullOrWhiteSpace(tags))
        {
            return null;
        }

        var parsed = tags
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(static tag => !string.IsNullOrWhiteSpace(tag))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        return parsed.Length == 0 ? null : parsed;
    }

    public static NtfyPublishRequest Create(NotificationSettings settings, NtfyNotificationPayload payload)
    {
        return new NtfyPublishRequest
        {
            Topic = NormalizeTopic(settings.Topic),
            Title = Trim(payload.Title, 160),
            Message = Trim(payload.Message, 1800),
            Priority = ResolvePriority(payload.Priority),
            Tags = ResolveTags(payload.Tags)
        };
    }

    private static string Trim(string? value, int maxLength)
    {
        var text = value?.Trim() ?? string.Empty;
        return text.Length <= maxLength ? text : text[..maxLength];
    }
}
