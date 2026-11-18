using System.Net;
using EduFocus.Parent.Core;
using EduFocus.Parent.Core.Presentation;
using EduFocus.Parent.Core.Push;
using EduFocus.Parent.Core.Services;
using EduFocus.Parent.Core.Settings;
using EduFocus.Parent.Tests.TestSupport;

namespace EduFocus.Parent.Tests.Push;

public sealed class PushRegistrationServiceTests
{
    private const string BaseUrl = "https://cloud.example.com";
    private const string FcmToken = "fcm-token-abcdef123456";
    private const string SuccessBody = "{\"updatedAtUtc\":\"2026-11-17T10:00:00Z\"}";

    [Fact]
    public async Task Registration_posts_token_and_stores_it()
    {
        var (service, handler, settings) = CreateService(_ => Success());

        var registered = await service.RegisterAsync(FcmToken, "android", "0.1.0");

        Assert.True(registered);
        var request = Assert.Single(handler.Requests);
        Assert.Equal(HttpMethod.Post, request.Method);
        Assert.Equal($"{BaseUrl}/v1/push/registrations", request.RequestUri?.ToString());
        Assert.Equal("Bearer", request.Headers.Authorization?.Scheme);
        Assert.Equal("efm_token", request.Headers.Authorization?.Parameter);

        var body = handler.RequestBodies[0];
        Assert.Contains($"\"fcmToken\":\"{FcmToken}\"", body);
        Assert.Contains("\"platform\":\"android\"", body);
        Assert.Contains("\"appVersion\":\"0.1.0\"", body);

        Assert.Equal(FcmToken, settings.PushToken);
        Assert.Equal("0.1.0", settings.PushAppVersion);
    }

    [Fact]
    public async Task Not_linked_device_does_not_call_api()
    {
        var (service, handler, settings) = CreateService(_ => Success(), linked: false);

        Assert.False(await service.RegisterAsync(FcmToken, "android", "0.1.0"));
        Assert.Empty(handler.Requests);
        Assert.Null(settings.PushToken);
    }

    [Fact]
    public async Task Same_token_is_not_sent_twice()
    {
        var (service, handler, _) = CreateService(_ => Success());

        Assert.True(await service.RegisterAsync(FcmToken, "android", "0.1.0"));
        Assert.True(await service.RegisterAsync(FcmToken, "android", "0.1.0"));

        Assert.Single(handler.Requests);
    }

    [Fact]
    public async Task Force_resends_the_same_token()
    {
        var (service, handler, _) = CreateService(_ => Success());

        Assert.True(await service.RegisterAsync(FcmToken, "android", "0.1.0"));
        Assert.True(await service.RegisterAsync(FcmToken, "android", "0.1.0", force: true));

        Assert.Equal(2, handler.Requests.Count);
    }

    [Fact]
    public async Task Network_error_retries_then_succeeds()
    {
        var attempts = 0;
        var waits = new List<TimeSpan>();
        var (service, handler, settings) = CreateService(
            _ =>
            {
                attempts++;
                return attempts == 1
                    ? throw new HttpRequestException("offline")
                    : Success();
            },
            waits: waits);

        Assert.True(await service.RegisterAsync(FcmToken, "android", "0.1.0"));
        Assert.Equal(2, handler.Requests.Count);
        Assert.Equal(new[] { TimeSpan.FromSeconds(2) }, waits);
        Assert.Equal(FcmToken, settings.PushToken);
    }

    [Fact]
    public async Task Server_errors_retry_then_give_up()
    {
        var waits = new List<TimeSpan>();
        var (service, handler, settings) = CreateService(
            _ => HttpStubs.Json(HttpStatusCode.ServiceUnavailable, "{\"error\":{\"code\":\"unavailable\"}}"),
            waits: waits);

        Assert.False(await service.RegisterAsync(FcmToken, "android", "0.1.0"));
        Assert.Equal(3, handler.Requests.Count);
        Assert.Equal(
            new[] { TimeSpan.FromSeconds(2), TimeSpan.FromSeconds(5) },
            waits);
        Assert.Null(settings.PushToken);
    }

    [Fact]
    public async Task Unauthorized_is_not_retried_and_triggers_session_expiry()
    {
        var waits = new List<TimeSpan>();
        var (service, handler, _, session, state) = CreateServiceWithSession(
            _ => HttpStubs.Json(HttpStatusCode.Unauthorized, "{\"error\":{\"code\":\"invalid_token\"}}"),
            waits: waits);
        var expiredCount = 0;
        session.Expired += () => expiredCount++;

        Assert.False(await service.RegisterAsync(FcmToken, "android", "0.1.0"));
        Assert.Single(handler.Requests);
        Assert.Empty(waits);
        Assert.Equal(1, expiredCount);
        Assert.False(state.IsLinked);
    }

    [Theory]
    [InlineData(HttpStatusCode.BadRequest)]
    [InlineData(HttpStatusCode.Forbidden)]
    [InlineData(HttpStatusCode.NotFound)]
    public async Task Client_config_errors_do_not_expire_session(HttpStatusCode statusCode)
    {
        var (service, handler, _, session, state) = CreateServiceWithSession(
            _ => HttpStubs.Json(statusCode, "{\"error\":{\"code\":\"bad_request\"}}"));
        var expiredCount = 0;
        session.Expired += () => expiredCount++;

        Assert.False(await service.RegisterAsync(FcmToken, "android", "0.1.0"));
        Assert.Single(handler.Requests);
        Assert.Equal(0, expiredCount);
        Assert.True(state.IsLinked);
    }

    [Fact]
    public async Task Rate_limit_honors_retry_after()
    {
        var attempts = 0;
        var waits = new List<TimeSpan>();
        var (service, handler, _) = CreateService(
            _ => ++attempts == 1
                ? HttpStubs.RateLimited("{\"error\":{\"code\":\"rate_limited\"}}", retryAfterSeconds: 30)
                : Success(),
            waits: waits);

        Assert.True(await service.RegisterAsync(FcmToken, "android", "0.1.0"));
        Assert.Equal(2, handler.Requests.Count);
        Assert.Equal(new[] { TimeSpan.FromSeconds(30) }, waits);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("bad\u0007token")]
    public async Task Invalid_tokens_are_rejected_without_network(string token)
    {
        var (service, handler, _) = CreateService(_ => Success());

        Assert.False(await service.RegisterAsync(token, "android", "0.1.0"));
        Assert.Empty(handler.Requests);
    }

    [Fact]
    public async Task Oversized_token_is_rejected_without_network()
    {
        var (service, handler, _) = CreateService(_ => Success());

        Assert.False(await service.RegisterAsync(new string('x', 513), "android", "0.1.0"));
        Assert.Empty(handler.Requests);
    }

    private static (PushRegistrationService Service, RecordingHttpMessageHandler Handler, ParentSettingsStore Settings)
        CreateService(
            Func<HttpRequestMessage, HttpResponseMessage> responder,
            bool linked = true,
            List<TimeSpan>? waits = null)
    {
        var (service, handler, settings, _, _) = CreateServiceWithSession(responder, linked, waits);
        return (service, handler, settings);
    }

    private static (
        PushRegistrationService Service,
        RecordingHttpMessageHandler Handler,
        ParentSettingsStore Settings,
        SessionExpiryService Session,
        AppState State)
        CreateServiceWithSession(
            Func<HttpRequestMessage, HttpResponseMessage> responder,
            bool linked = true,
            List<TimeSpan>? waits = null)
    {
        var handler = new RecordingHttpMessageHandler(responder);
        var state = new AppState
        {
            CloudBaseUrl = BaseUrl,
            IsLinked = linked,
        };
        var kv = new FakeKeyValueStore();
        var settings = new ParentSettingsStore(kv);
        var tokens = new MemoryTokenStore("efm_token");
        var session = new SessionExpiryService(tokens, state, settings, new ParentDataCache(kv));
        var api = new CloudApiClient(
            new RecordingHttpClientFactory(handler),
            tokens,
            state);
        var service = new PushRegistrationService(
            api,
            state,
            settings,
            session,
            waits is null
                ? null
                : (delay, _) =>
                {
                    waits.Add(delay);
                    return Task.CompletedTask;
                });

        return (service, handler, settings, session, state);
    }

    private static HttpResponseMessage Success() =>
        HttpStubs.Json(HttpStatusCode.OK, SuccessBody);
}
