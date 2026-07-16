using System.Net;
using System.Text;
using System.Text.Json;
using NetworkHealthMonitor.Models;
using NetworkHealthMonitor.Services;
using Xunit;

namespace NetworkHealthMonitor.Tests;

public sealed class NtfyNotificationClientTests
{
    [Fact]
    public void Publish_request_factory_serializes_valid_json_with_array_tags_and_numeric_priority()
    {
        var request = NtfyPublishRequestFactory.Create(
            new NotificationSettings { Topic = " network-health-monitor123 " },
            new NtfyNotificationPayload
            {
                Title = "Başlık \"özel\"",
                Message = "Satır1\nSatır2 \"tırnak\"",
                Priority = "default",
                Tags = "white_check_mark,bell"
            });

        var json = JsonSerializer.Serialize(request);
        using var document = JsonDocument.Parse(json);
        Assert.Equal("network-health-monitor123", document.RootElement.GetProperty("topic").GetString());
        Assert.Equal(3, document.RootElement.GetProperty("priority").GetInt32());
        Assert.Equal(JsonValueKind.Array, document.RootElement.GetProperty("tags").ValueKind);
        Assert.Contains("Başlık", document.RootElement.GetProperty("title").GetString());
        Assert.Contains("\n", document.RootElement.GetProperty("message").GetString());
    }

    [Theory]
    [InlineData("high", 4)]
    [InlineData("default", 3)]
    [InlineData("2", 2)]
    [InlineData("urgent", 5)]
    public void Priority_mapping_is_stable(string input, int expected)
    {
        Assert.Equal(expected, NtfyPublishRequestFactory.ResolvePriority(input));
    }

    [Fact]
    public void Topic_validation_rejects_path_characters()
    {
        Assert.False(NtfyPublishRequestFactory.IsTopicValid(""));
        Assert.False(NtfyPublishRequestFactory.IsTopicValid("a/b"));
        Assert.True(NtfyPublishRequestFactory.IsTopicValid("network-health-monitor123"));
    }

    [Fact]
    public void Server_url_trims_trailing_slash_before_parse()
    {
        var uri = NtfyPublishRequestFactory.NormalizeBaseUri("https://ntfy.sh/");
        Assert.Equal("https://ntfy.sh/", uri.AbsoluteUri);
        Assert.Equal(Uri.UriSchemeHttps, uri.Scheme);
        Assert.Equal(string.Empty, uri.AbsolutePath.Trim('/'));
    }

    [Fact]
    public async Task Publish_sends_json_body_without_authorization_when_token_empty()
    {
        HttpRequestMessage? captured = null;
        string? body = null;
        var client = new NtfyNotificationClient(
            new CapturingHttpClientFactory(async request =>
            {
                captured = request;
                body = request.Content is null ? null : await request.Content.ReadAsStringAsync();
                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent("{\"id\":\"1\"}", Encoding.UTF8, "application/json")
                };
            }),
            new DpapiSecretProtector());

        var result = await client.PublishAsync(
            new NotificationSettings
            {
                Enabled = true,
                BaseUrl = "https://ntfy.sh",
                Topic = "network-health-monitor123",
                AccessToken = "   "
            },
            new NtfyNotificationPayload
            {
                Title = "NetworkHealthMonitor test bildirimi",
                Message = "Bildirim ayarlarınız başarıyla doğrulandı.",
                Priority = "default",
                Tags = "white_check_mark"
            });

        Assert.True(result.Success);
        Assert.NotNull(captured);
        Assert.Equal(HttpMethod.Post, captured!.Method);
        Assert.Equal("https://ntfy.sh/", captured.RequestUri!.AbsoluteUri);
        Assert.Equal("application/json", captured.Content!.Headers.ContentType!.MediaType);
        Assert.Null(captured.Headers.Authorization);
        Assert.False(string.IsNullOrWhiteSpace(body));
        using var document = JsonDocument.Parse(body!);
        Assert.Equal("network-health-monitor123", document.RootElement.GetProperty("topic").GetString());
        Assert.Equal(3, document.RootElement.GetProperty("priority").GetInt32());
        Assert.Equal(JsonValueKind.Array, document.RootElement.GetProperty("tags").ValueKind);
    }

    [Fact]
    public async Task Publish_adds_bearer_authorization_when_token_present()
    {
        HttpRequestMessage? captured = null;
        var protector = new DpapiSecretProtector();
        var protectedToken = protector.Protect("secret-token-value");
        var client = new NtfyNotificationClient(
            new CapturingHttpClientFactory(request =>
            {
                captured = request;
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK));
            }),
            protector);

        await client.PublishAsync(
            new NotificationSettings
            {
                Enabled = true,
                BaseUrl = "https://ntfy.example",
                Topic = "topic",
                AccessToken = protectedToken
            },
            new NtfyNotificationPayload { Title = "t", Message = "m", Priority = "high", Tags = "warning" });

        Assert.NotNull(captured);
        Assert.Equal("Bearer", captured!.Headers.Authorization!.Scheme);
        Assert.Equal("secret-token-value", captured.Headers.Authorization.Parameter);
    }

    [Fact]
    public async Task Publish_maps_http_400_to_turkish_user_message()
    {
        var client = new NtfyNotificationClient(
            new CapturingHttpClientFactory(_ => Task.FromResult(new HttpResponseMessage(HttpStatusCode.BadRequest)
            {
                Content = new StringContent(
                    """{"code":40024,"http":400,"error":"invalid request: request body must be valid JSON"}""",
                    Encoding.UTF8,
                    "application/json")
            })),
            new DpapiSecretProtector());

        var result = await client.PublishAsync(
            new NotificationSettings { Enabled = true, BaseUrl = "https://ntfy.sh", Topic = "topic" },
            new NtfyNotificationPayload { Title = "t", Message = "m" });

        Assert.False(result.Success);
        Assert.Equal(400, result.StatusCode);
        Assert.Contains("geçersiz", result.UserMessage, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("40024", result.UserMessage, StringComparison.Ordinal);
        Assert.Contains("40024", result.TechnicalDetail, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(HttpStatusCode.Unauthorized)]
    [InlineData(HttpStatusCode.Forbidden)]
    public async Task Publish_maps_auth_failures(HttpStatusCode statusCode)
    {
        var client = new NtfyNotificationClient(
            new CapturingHttpClientFactory(_ => Task.FromResult(new HttpResponseMessage(statusCode))),
            new DpapiSecretProtector());

        var result = await client.PublishAsync(
            new NotificationSettings { Enabled = true, BaseUrl = "https://ntfy.sh", Topic = "topic", AccessToken = "tk_test" },
            new NtfyNotificationPayload { Title = "t", Message = "m" });

        Assert.False(result.Success);
        Assert.False(result.IsTransient);
        Assert.Contains("Erişim anahtarı", result.UserMessage, StringComparison.Ordinal);
        Assert.DoesNotContain("tk_test", result.UserMessage, StringComparison.Ordinal);
        Assert.DoesNotContain("tk_test", result.TechnicalDetail, StringComparison.Ordinal);
        Assert.DoesNotContain("tk_test", result.SafeErrorMessage, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Publish_maps_timeout()
    {
        var client = new NtfyNotificationClient(
            new CapturingHttpClientFactory(async (request, cancellationToken) =>
            {
                await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
                return new HttpResponseMessage(HttpStatusCode.OK);
            }),
            new DpapiSecretProtector());

        var result = await client.PublishAsync(
            new NotificationSettings
            {
                Enabled = true,
                BaseUrl = "https://ntfy.sh",
                Topic = "topic",
                RequestTimeoutSeconds = 1
            },
            new NtfyNotificationPayload { Title = "t", Message = "m" });

        Assert.False(result.Success);
        Assert.True(result.IsTransient);
        Assert.Equal(NtfyFailureKind.Timeout, result.FailureKind);
        Assert.Contains("zamanında", result.UserMessage, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Publish_maps_network_failure()
    {
        var client = new NtfyNotificationClient(
            new CapturingHttpClientFactory(_ => throw new HttpRequestException("No such host is known.")),
            new DpapiSecretProtector());

        var result = await client.PublishAsync(
            new NotificationSettings { Enabled = true, BaseUrl = "https://ntfy.sh", Topic = "topic" },
            new NtfyNotificationPayload { Title = "t", Message = "m" });

        Assert.False(result.Success);
        Assert.True(result.IsTransient);
        Assert.Contains("ulaşılamadı", result.UserMessage, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Publish_honors_cancellation_token()
    {
        var client = new NtfyNotificationClient(
            new CapturingHttpClientFactory(async (request, cancellationToken) =>
            {
                await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
                return new HttpResponseMessage(HttpStatusCode.OK);
            }),
            new DpapiSecretProtector());

        using var cts = new CancellationTokenSource(50);
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => client.PublishAsync(
            new NotificationSettings
            {
                Enabled = true,
                BaseUrl = "https://ntfy.sh",
                Topic = "topic",
                RequestTimeoutSeconds = 30
            },
            new NtfyNotificationPayload { Title = "t", Message = "m" },
            cts.Token));
    }

    [Fact]
    public void User_message_mapper_sanitizes_tokens()
    {
        var sanitized = NtfyUserMessageMapper.Sanitize("Authorization: Bearer abc.def.ghi failed");
        Assert.DoesNotContain("abc.def.ghi", sanitized, StringComparison.Ordinal);
        Assert.Contains("[redacted]", sanitized, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Live_ntfy_public_topic_publish_succeeds_without_token()
    {
        if (!string.Equals(Environment.GetEnvironmentVariable("NHM_LIVE_NTFY"), "1", StringComparison.Ordinal))
        {
            return;
        }

        var topic = $"nhm-live-{Guid.NewGuid():N}"[..12];
        var client = new NtfyNotificationClient(new DefaultHttpClientFactory(), new DpapiSecretProtector());
        var result = await client.PublishAsync(
            new NotificationSettings
            {
                Enabled = true,
                BaseUrl = "https://ntfy.sh",
                Topic = topic,
                AccessToken = string.Empty
            },
            new NtfyNotificationPayload
            {
                Title = "NetworkHealthMonitor test bildirimi",
                Message = "Bildirim ayarlarınız başarıyla doğrulandı.",
                Priority = "default",
                Tags = "white_check_mark"
            });

        Assert.True(result.Success, result.TechnicalDetail);
        Assert.Contains("başarıyla", result.UserMessage, StringComparison.OrdinalIgnoreCase);
    }

    private sealed class CapturingHttpClientFactory : IHttpClientFactory
    {
        private readonly Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> _handler;

        public CapturingHttpClientFactory(Func<HttpRequestMessage, Task<HttpResponseMessage>> handler)
            : this((request, _) => handler(request))
        {
        }

        public CapturingHttpClientFactory(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> handler)
        {
            _handler = handler;
        }

        public HttpClient CreateClient(string name)
        {
            return new HttpClient(new AsyncDelegateHandler(_handler));
        }
    }

    private sealed class AsyncDelegateHandler : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> _handler;

        public AsyncDelegateHandler(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> handler)
        {
            _handler = handler;
        }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            return _handler(request, cancellationToken);
        }
    }
}
