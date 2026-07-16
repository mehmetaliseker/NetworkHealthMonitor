using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using NetworkHealthMonitor.Models;

namespace NetworkHealthMonitor.Services;

public sealed class NtfyNotificationClient : INtfyNotificationClient
{
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        PropertyNamingPolicy = null,
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull
    };

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
            return CreateValidationFailure(
                "Bildirimler kapalı.\nTest için ntfy bildirimlerini etkinleştirin veya test gönderimi ayarları geçici olarak açar.",
                NtfyFailureKind.Disabled,
                isTransient: true);
        }

        var topic = NtfyPublishRequestFactory.NormalizeTopic(settings.Topic);
        if (!NtfyPublishRequestFactory.IsTopicValid(topic))
        {
            return CreateValidationFailure(
                "Konu adı geçersiz.\nKonu boş olamaz ve /, ? veya # karakterleri içeremez.",
                NtfyFailureKind.Validation);
        }

        Uri baseUri;
        try
        {
            baseUri = NtfyPublishRequestFactory.NormalizeBaseUri(settings.BaseUrl);
        }
        catch (ArgumentException)
        {
            return CreateValidationFailure(
                "Sunucu adresi geçersiz.\nhttp:// veya https:// ile başlayan geçerli bir adres girin.",
                NtfyFailureKind.Validation);
        }

        if (baseUri.Scheme != Uri.UriSchemeHttps && !(settings.AllowInsecureHttp && baseUri.Scheme == Uri.UriSchemeHttp))
        {
            return CreateValidationFailure(
                "Sunucu adresi güvenli olmalıdır.\nGenel kullanım için https:// kullanın.",
                NtfyFailureKind.Validation);
        }

        if (LooksLikeTopicInUrl(baseUri, topic))
        {
            return CreateValidationFailure(
                "Sunucu adresine konu eklemeyin.\nSunucu alanı yalnızca https://ntfy.sh gibi kök adresi içermelidir; konu ayrı alana yazılır.",
                NtfyFailureKind.Validation);
        }

        var requestDto = NtfyPublishRequestFactory.Create(settings, payload);
        var json = JsonSerializer.Serialize(requestDto, SerializerOptions);

        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutCts.CancelAfter(TimeSpan.FromSeconds(Math.Clamp(settings.RequestTimeoutSeconds, 1, 120)));

        try
        {
            using var client = _httpClientFactory.CreateClient("NetworkHealthMonitor.ntfy");
            client.Timeout = Timeout.InfiniteTimeSpan;

            using var request = new HttpRequestMessage(HttpMethod.Post, baseUri);
            request.Content = new StringContent(json, Encoding.UTF8, "application/json");

            var token = (_secretProtector.Unprotect(settings.AccessToken) ?? string.Empty).Trim();
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
            var technical = NtfyUserMessageMapper.CreateTechnicalDetail((int)response.StatusCode, body, NtfyFailureKind.Http);
            var failure = new NtfyPublishResult(
                false,
                IsTransient(response.StatusCode),
                (int)response.StatusCode,
                response.Headers.RetryAfter?.Delta,
                technical,
                string.Empty,
                technical,
                NtfyFailureKind.Http);

            var userMessage = NtfyUserMessageMapper.CreateUserMessage(failure);
            return failure with
            {
                SafeErrorMessage = userMessage,
                UserMessage = userMessage
            };
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            var technical = NtfyUserMessageMapper.CreateTechnicalDetail(null, "Request timed out.", NtfyFailureKind.Timeout);
            var userMessage = NtfyUserMessageMapper.CreateUserMessage(new NtfyPublishResult(
                false, true, null, null, technical, string.Empty, technical, NtfyFailureKind.Timeout));
            return new NtfyPublishResult(false, true, null, null, userMessage, userMessage, technical, NtfyFailureKind.Timeout);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (HttpRequestException ex)
        {
            var technical = NtfyUserMessageMapper.CreateTechnicalDetail(
                null,
                NtfyUserMessageMapper.Sanitize(ex.Message),
                NtfyFailureKind.Network);
            var userMessage = NtfyUserMessageMapper.CreateUserMessage(new NtfyPublishResult(
                false, true, null, null, technical, string.Empty, technical, NtfyFailureKind.Network));
            return new NtfyPublishResult(false, true, null, null, userMessage, userMessage, technical, NtfyFailureKind.Network);
        }
    }

    private static bool LooksLikeTopicInUrl(Uri baseUri, string topic)
    {
        if (string.IsNullOrWhiteSpace(baseUri.AbsolutePath) || baseUri.AbsolutePath == "/")
        {
            return false;
        }

        var path = baseUri.AbsolutePath.Trim('/');
        return path.Equals(topic, StringComparison.OrdinalIgnoreCase)
            || path.Contains('/', StringComparison.Ordinal);
    }

    private static NtfyPublishResult CreateValidationFailure(string userMessage, string failureKind, bool isTransient = false)
    {
        var technical = NtfyUserMessageMapper.CreateTechnicalDetail(null, userMessage, failureKind);
        return new NtfyPublishResult(
            false,
            isTransient,
            null,
            null,
            userMessage,
            userMessage,
            technical,
            failureKind);
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
        return NtfyUserMessageMapper.Sanitize(body.ReplaceLineEndings(" ").Trim());
    }
}
