using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using NetworkHealthMonitor.Models;

namespace NetworkHealthMonitor.Services;

public sealed class NtfyNotificationClient : INtfyNotificationClient
{
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ISecretProtector _secretProtector;

    public NtfyNotificationClient(IHttpClientFactory httpClientFactory, ISecretProtector secretProtector)
    {
        _httpClientFactory = httpClientFactory;
        _secretProtector = secretProtector;
    }

    public async Task<NtfyPublishResult> PublishAsync(
        NotificationSettings settings,
        NtfyNotificationPayload payload,
        CancellationToken cancellationToken = default)
    {
        if (!settings.Enabled)
        {
            return new NtfyPublishResult(false, true, null, null, "Notifications are disabled.");
        }

        if (string.IsNullOrWhiteSpace(settings.Topic))
        {
            return new NtfyPublishResult(false, false, null, null, "Notification topic is empty.");
        }

        if (!Uri.TryCreate(settings.BaseUrl, UriKind.Absolute, out var baseUri))
        {
            return new NtfyPublishResult(false, false, null, null, "Notification server URL is invalid.");
        }

        if (baseUri.Scheme != Uri.UriSchemeHttps && !(settings.AllowInsecureHttp && baseUri.Scheme == Uri.UriSchemeHttp))
        {
            return new NtfyPublishResult(false, false, null, null, "Notification server must use HTTPS unless insecure HTTP is explicitly allowed.");
        }

        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutCts.CancelAfter(TimeSpan.FromSeconds(Math.Clamp(settings.RequestTimeoutSeconds, 1, 120)));

        try
        {
            using var client = _httpClientFactory.CreateClient("NetworkHealthMonitor.ntfy");
            client.BaseAddress = baseUri;
            client.Timeout = Timeout.InfiniteTimeSpan;

            using var request = new HttpRequestMessage(HttpMethod.Post, string.Empty);
            request.Content = JsonContent.Create(new
            {
                topic = settings.Topic,
                title = Trim(payload.Title, 160),
                message = Trim(payload.Message, 1800),
                priority = payload.Priority,
                tags = payload.Tags
            });

            var token = _secretProtector.Unprotect(settings.AccessToken);
            if (!string.IsNullOrWhiteSpace(token))
            {
                request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
            }

            using var response = await client.SendAsync(request, timeoutCts.Token);
            if (response.IsSuccessStatusCode)
            {
                return NtfyPublishResult.Ok();
            }

            var body = await ReadSafeBodyAsync(response, timeoutCts.Token);
            return new NtfyPublishResult(
                false,
                IsTransient(response.StatusCode),
                (int)response.StatusCode,
                response.Headers.RetryAfter?.Delta,
                string.IsNullOrWhiteSpace(body)
                    ? $"HTTP {(int)response.StatusCode}"
                    : $"HTTP {(int)response.StatusCode}: {body}");
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return new NtfyPublishResult(false, true, null, null, "Notification request timed out.");
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (HttpRequestException ex)
        {
            return new NtfyPublishResult(false, true, null, null, Trim(ex.Message, 500));
        }
    }

    private static bool IsTransient(HttpStatusCode statusCode)
    {
        return statusCode is HttpStatusCode.RequestTimeout
            or HttpStatusCode.TooManyRequests
            or >= HttpStatusCode.InternalServerError;
    }

    private static async Task<string> ReadSafeBodyAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        var body = await response.Content.ReadAsStringAsync(cancellationToken);
        return Trim(body.ReplaceLineEndings(" "), 500);
    }

    private static string Trim(string value, int maxLength)
    {
        var text = value?.Trim() ?? string.Empty;
        return text.Length <= maxLength ? text : text[..maxLength];
    }
}
