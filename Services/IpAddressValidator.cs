using System.Net;
using System.Net.Sockets;

namespace NetworkHealthMonitor.Services;

public readonly record struct DeviceAddressValidationResult(
    bool IsValid,
    string NormalizedAddress,
    string ErrorMessage);

public static class IpAddressValidator
{
    public static bool IsValidIpv4(string? value)
    {
        return TryNormalizeIpv4(value, out _);
    }

    public static DeviceAddressValidationResult ValidateDeviceAddress(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return Invalid("IP adresi veya hostname boş olamaz.");
        }

        var trimmed = value.Trim();
        if (trimmed.Length > 253)
        {
            return Invalid("IP adresi veya hostname 253 karakterden uzun olamaz.");
        }

        if (trimmed.StartsWith("http://", StringComparison.OrdinalIgnoreCase)
            || trimmed.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
        {
            return Invalid("IP adresi veya hostname girin. http:// veya https:// kullanmayın.");
        }

        if (Uri.TryCreate(trimmed, UriKind.Absolute, out var uri) && !string.IsNullOrWhiteSpace(uri.Scheme))
        {
            return Invalid("IP adresi veya hostname girin. URL formatı kullanmayın.");
        }

        if (TryNormalizeIpv4(trimmed, out var normalizedIp))
        {
            return Valid(normalizedIp);
        }

        if (LooksLikeIpv4(trimmed))
        {
            return Invalid("Geçersiz IP adresi.");
        }

        if (TryNormalizeHostname(trimmed, out var normalizedHost))
        {
            return Valid(normalizedHost);
        }

        return Invalid("Geçerli bir IP adresi veya hostname girin.");
    }

    public static bool IsValidDeviceAddress(string? value)
    {
        return ValidateDeviceAddress(value).IsValid;
    }

    private static bool TryNormalizeIpv4(string? value, out string normalizedIp)
    {
        normalizedIp = string.Empty;
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        var trimmed = value.Trim();
        var parts = trimmed.Split('.');
        if (parts.Length != 4
            || !IPAddress.TryParse(trimmed, out var address)
            || address.AddressFamily != AddressFamily.InterNetwork)
        {
            return false;
        }

        var bytes = new byte[4];
        for (var index = 0; index < parts.Length; index++)
        {
            if (parts[index].Length == 0
                || !byte.TryParse(parts[index], out bytes[index])
                || !string.Equals(parts[index], bytes[index].ToString(), StringComparison.Ordinal))
            {
                return false;
            }
        }

        normalizedIp = string.Join('.', bytes);
        return true;
    }

    private static bool LooksLikeIpv4(string value)
    {
        return value.Count(character => character == '.') == 3
            && value.All(character => char.IsAsciiDigit(character) || character == '.');
    }

    private static bool TryNormalizeHostname(string value, out string normalizedHost)
    {
        normalizedHost = string.Empty;

        if (value.Any(char.IsWhiteSpace)
            || value.IndexOfAny(new[] { '/', '\\', ':', '?', '#', '@' }) >= 0
            || !value.Any(char.IsAsciiLetter))
        {
            return false;
        }

        var labels = value.Split('.');
        if (labels.Length == 0 || labels.Any(string.IsNullOrWhiteSpace))
        {
            return false;
        }

        foreach (var label in labels)
        {
            if (label.Length is < 1 or > 63
                || label[0] == '-'
                || label[^1] == '-'
                || !label.All(character => char.IsAsciiLetterOrDigit(character) || character == '-'))
            {
                return false;
            }
        }

        normalizedHost = value.ToLowerInvariant();
        return true;
    }

    private static DeviceAddressValidationResult Valid(string normalizedAddress)
    {
        return new DeviceAddressValidationResult(true, normalizedAddress, string.Empty);
    }

    private static DeviceAddressValidationResult Invalid(string message)
    {
        return new DeviceAddressValidationResult(false, string.Empty, message);
    }
}
