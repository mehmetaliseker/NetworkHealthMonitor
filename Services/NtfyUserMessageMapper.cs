using System.Net;
using System.Text.Json;
using System.Text.RegularExpressions;
using NetworkHealthMonitor.Models;

namespace NetworkHealthMonitor.Services;

public static class NtfyUserMessageMapper
{
    private static readonly Regex TokenPattern = new(
        @"Bearer\s+[A-Za-z0-9_\-\.]+|tk_[A-Za-z0-9_\-]+",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    public static string CreateUserMessage(NtfyPublishResult result)
    {
        if (result.Success)
        {
            return "Test bildirimi başarıyla gönderildi.";
        }

        if (result.StatusCode is (int)HttpStatusCode.Unauthorized or (int)HttpStatusCode.Forbidden)
        {
            return "Bildirim gönderilemedi.\nErişim anahtarı geçersiz veya bu konuya gönderim yetkiniz bulunmuyor.";
        }

        if (result.StatusCode == (int)HttpStatusCode.BadRequest)
        {
            return "Bildirim gönderilemedi.\nSunucu gönderilen isteği geçersiz buldu. Bildirim ayarlarını kontrol edin.";
        }

        if (result.StatusCode == (int)HttpStatusCode.TooManyRequests)
        {
            return "Bildirim gönderilemedi.\nSunucu çok fazla istek nedeniyle isteği reddetti. Bir süre sonra yeniden deneyin.";
        }

        if (result.StatusCode is >= 500 and <= 599)
        {
            return "Bildirim gönderilemedi.\nBildirim sunucusunda geçici bir sorun oluştu. Daha sonra yeniden deneyin.";
        }

        if (string.Equals(result.FailureKind, NtfyFailureKind.Timeout, StringComparison.Ordinal))
        {
            return "Bildirim sunucusu zamanında yanıt vermedi.\nSunucu adresini ve internet bağlantısını kontrol edin.";
        }

        if (string.Equals(result.FailureKind, NtfyFailureKind.Network, StringComparison.Ordinal))
        {
            return "Bildirim sunucusuna ulaşılamadı.\nİnternet bağlantısını ve sunucu adresini kontrol edin.";
        }

        if (string.Equals(result.FailureKind, NtfyFailureKind.Validation, StringComparison.Ordinal))
        {
            return string.IsNullOrWhiteSpace(result.UserMessage)
                ? "Bildirim ayarları geçersiz.\nSunucu adresi ve konu alanlarını kontrol edin."
                : result.UserMessage;
        }

        return string.IsNullOrWhiteSpace(result.UserMessage)
            ? "Bildirim gönderilemedi.\nSunucu adresini, konu adını ve internet bağlantısını kontrol edin."
            : result.UserMessage;
    }

    public static string CreateTechnicalDetail(int? statusCode, string? responseBody, string? failureKind = null)
    {
        var parts = new List<string>();
        if (statusCode.HasValue)
        {
            parts.Add($"HTTP durum kodu: {statusCode.Value}");
        }

        if (!string.IsNullOrWhiteSpace(failureKind))
        {
            parts.Add($"Hata türü: {failureKind}");
        }

        var sanitizedBody = Sanitize(responseBody);
        if (!string.IsNullOrWhiteSpace(sanitizedBody))
        {
            TryAppendNtfyCode(parts, sanitizedBody);
            parts.Add($"Açıklama: {Trim(sanitizedBody, 400)}");
        }

        parts.Add($"İstek zamanı: {DateTime.Now:dd.MM.yyyy HH:mm:ss}");
        return string.Join(Environment.NewLine, parts);
    }

    public static string Sanitize(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        return TokenPattern.Replace(value, "[redacted]");
    }

    private static void TryAppendNtfyCode(ICollection<string> parts, string body)
    {
        try
        {
            using var document = JsonDocument.Parse(body);
            if (document.RootElement.TryGetProperty("code", out var codeElement)
                && codeElement.ValueKind == JsonValueKind.Number)
            {
                parts.Add($"ntfy hata kodu: {codeElement.GetInt32()}");
            }

            if (document.RootElement.TryGetProperty("error", out var errorElement)
                && errorElement.ValueKind == JsonValueKind.String)
            {
                parts.Add($"Sunucu mesajı: {errorElement.GetString()}");
            }
        }
        catch (JsonException)
        {
            // Ham gövde zaten açıklama olarak eklenir.
        }
    }

    private static string Trim(string value, int maxLength)
    {
        var text = value.Trim();
        return text.Length <= maxLength ? text : text[..maxLength];
    }
}

public static class NtfyFailureKind
{
    public const string Validation = "Validation";
    public const string Timeout = "Timeout";
    public const string Network = "Network";
    public const string Http = "Http";
    public const string Disabled = "Disabled";
}
