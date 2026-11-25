using System.Net;
using EduFocus.Parent.Core;
using EduFocus.Parent.Core.Pairing;
using EduFocus.Parent.Core.Presentation;
using EduFocus.Parent.Core.Services;
using EduFocus.Parent.Core.Settings;
using EduFocus.Parent.Tests.TestSupport;

namespace EduFocus.Parent.Tests.Pairing;

public sealed class PairingServiceTests
{
    private const string NewFamilyId = "2b9f6f70-1f61-4d3e-9d7d-3a2f6b5c4d11";
    private const string NewDeviceId = "8e2c6b1a-7f3d-4a55-8c1f-0d9e7b6a5c33";
    private const string OldDeviceId = "0b5c9a5e-8f4a-4c6f-9f0b-2d3e4f5a6b7c";
    private const string SessionId = "6f7d1a6e-2f4f-4e3b-9b8e-7c1f5c0a1b2c";

    [Fact]
    public async Task Claim_stores_token_and_link_state()
    {
        var (service, handler, tokens, state, settings, _) = CreateService(_ => Claimed());

        var response = await service.ClaimAsync(
            new PairingClaim("abcd-2345", "https://cloud.example.com", Guid.Parse(SessionId)),
            Identity(),
            replaceExisting: false);

        Assert.Equal("efm_new-token", response.MobileToken);
        Assert.Equal("efm_new-token", tokens.Token);
        Assert.True(state.IsLinked);
        Assert.Equal(Guid.Parse(NewFamilyId), state.FamilyId);
        Assert.Equal(Guid.Parse(NewDeviceId), state.DeviceId);
        Assert.Equal("Student PC", state.DesktopName);
        Assert.Equal("Asia/Ho_Chi_Minh", state.TimeZone);
        Assert.Equal("https://cloud.example.com", state.CloudBaseUrl);
        Assert.Equal("https://cloud.example.com", settings.CloudBaseUrl);
        Assert.Equal(Guid.Parse(NewDeviceId), settings.DeviceId);

        var request = Assert.Single(handler.Requests);
        Assert.Equal(HttpMethod.Post, request.Method);
        Assert.Equal("https://cloud.example.com/v1/pairing/claim", request.RequestUri?.ToString());
        Assert.Null(request.Headers.Authorization);

        var body = handler.RequestBodies[0];
        Assert.Contains("\"code\":\"ABCD2345\"", body);
        Assert.Contains($"\"sessionId\":\"{SessionId}\"", body);
        Assert.Contains("\"deviceName\":\"Pixel test\"", body);
        Assert.Contains("\"platform\":\"android\"", body);
        Assert.Contains("\"timeZoneId\":\"Asia/Ho_Chi_Minh\"", body);
    }

    [Fact]
    public async Task Manual_claim_without_session_sends_no_session_id()
    {
        var (service, handler, _, _, _, _) = CreateService(_ => Claimed());

        await service.ClaimAsync(
            new PairingClaim("ABCD2345", "https://cloud.example.com", SessionId: null),
            Identity(),
            replaceExisting: false);

        Assert.DoesNotContain("\"sessionId\"", handler.RequestBodies[0]);
    }

    [Fact]
    public async Task Invalid_code_is_rejected_before_any_network_call()
    {
        var (service, handler, tokens, state, _, _) = CreateService(_ => Claimed());

        var exception = await Assert.ThrowsAsync<PairingException>(
            () => service.ClaimAsync(
                new PairingClaim("ABCD23", "https://cloud.example.com", null),
                Identity(),
                replaceExisting: false));

        Assert.Equal(PairingFailure.InvalidCode, exception.Failure);
        Assert.Empty(handler.Requests);
        Assert.Null(tokens.Token);
        Assert.False(state.IsLinked);
    }

    [Fact]
    public async Task Manual_claim_requires_server_url()
    {
        var (service, handler, _, _, _, _) = CreateService(_ => Claimed());

        var exception = await Assert.ThrowsAsync<PairingException>(
            () => service.ClaimAsync(
                new PairingClaim("ABCD2345", string.Empty, null),
                Identity(),
                replaceExisting: false));

        Assert.Equal(PairingFailure.Configuration, exception.Failure);
        Assert.Contains("địa chỉ máy chủ", exception.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Empty(handler.Requests);
    }

    [Fact]
    public async Task Http_is_rejected_outside_development()
    {
        var (service, _, _, _, _, _) = CreateService(_ => Claimed());

        var exception = await Assert.ThrowsAsync<PairingException>(
            () => service.ClaimAsync(
                new PairingClaim("ABCD2345", "http://192.168.1.20:5281", null),
                Identity(),
                replaceExisting: false));

        Assert.Equal(PairingFailure.Configuration, exception.Failure);
        Assert.Contains("HTTPS", exception.Message);
    }

    [Fact]
    public async Task Expired_code_maps_to_friendly_message()
    {
        var (service, _, tokens, _, _, _) = CreateService(_ => HttpStubs.Json(
            HttpStatusCode.Gone,
            """{"error":{"code":"pairing_unavailable","message":"Mã ghép nối không hợp lệ, đã dùng hoặc đã hết hạn."}}"""));

        var exception = await Assert.ThrowsAsync<PairingException>(
            () => service.ClaimAsync(
                new PairingClaim("ABCD2345", "https://cloud.example.com", null),
                Identity(),
                replaceExisting: false));

        Assert.Equal(PairingFailure.InvalidCode, exception.Failure);
        Assert.Contains("hết hạn", exception.Message);
        Assert.Null(tokens.Token);
    }

    [Fact]
    public async Task Rate_limit_exposes_retry_after()
    {
        var (service, _, _, _, _, _) = CreateService(_ => HttpStubs.RateLimited(
            """{"error":{"code":"rate_limited","message":"Quá nhiều yêu cầu. Vui lòng thử lại sau."}}""",
            120));

        var exception = await Assert.ThrowsAsync<PairingException>(
            () => service.ClaimAsync(
                new PairingClaim("ABCD2345", "https://cloud.example.com", null),
                Identity(),
                replaceExisting: false));

        Assert.Equal(PairingFailure.RateLimited, exception.Failure);
        Assert.Equal(TimeSpan.FromSeconds(120), exception.RetryAfter);
    }

    [Fact]
    public async Task Network_failure_maps_to_retryable_message()
    {
        var (service, _, _, _, _, _) = CreateService(_ => throw new HttpRequestException("offline"));

        var exception = await Assert.ThrowsAsync<PairingException>(
            () => service.ClaimAsync(
                new PairingClaim("ABCD2345", "https://cloud.example.com", null),
                Identity(),
                replaceExisting: false));

        Assert.Equal(PairingFailure.Network, exception.Failure);
        Assert.Contains("mạng", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Replace_claims_before_revoking_the_old_device()
    {
        var (service, handler, tokens, state, _, _) = CreateLinkedState(responder: request =>
            request.Method == HttpMethod.Delete
                ? HttpStubs.Json(HttpStatusCode.OK, """{"ok":true}""")
                : Claimed());

        await service.ClaimAsync(
            new PairingClaim("ABCD2345", "https://new.example.com", null),
            Identity(),
            replaceExisting: true);

        Assert.Equal(2, handler.Requests.Count);
        var claim = handler.Requests[0];
        Assert.Equal(HttpMethod.Post, claim.Method);
        Assert.Equal("https://new.example.com/v1/pairing/claim", claim.RequestUri?.ToString());
        Assert.Null(claim.Headers.Authorization);

        var revoke = handler.Requests[1];
        Assert.Equal(HttpMethod.Delete, revoke.Method);
        Assert.Equal($"https://cloud.example.com/v1/devices/{OldDeviceId}", revoke.RequestUri?.ToString());
        Assert.Equal("efm_old-token", revoke.Headers.Authorization?.Parameter);

        Assert.Equal("efm_new-token", tokens.Token);
        Assert.Equal(Guid.Parse(NewDeviceId), state.DeviceId);
        Assert.Equal("https://new.example.com", state.CloudBaseUrl);
    }

    [Fact]
    public async Task Replace_still_claims_when_old_token_revoke_fails()
    {
        var (service, handler, tokens, state, _, _) = CreateLinkedState(responder: request =>
            request.Method == HttpMethod.Delete
                ? HttpStubs.Json(HttpStatusCode.InternalServerError, "{}")
                : Claimed());

        await service.ClaimAsync(
            new PairingClaim("ABCD2345", "https://new.example.com", null),
            Identity(),
            replaceExisting: true);

        Assert.Equal(2, handler.Requests.Count);
        Assert.Equal("efm_new-token", tokens.Token);
        Assert.True(state.IsLinked);
        Assert.Equal(Guid.Parse(NewDeviceId), state.DeviceId);
    }

    [Fact]
    public async Task Successful_replacement_clears_the_previous_family_cache()
    {
        var (service, _, _, _, _, store) = CreateLinkedState(
            _ => Claimed(),
            seedCache: true);

        await service.ClaimAsync(
            new PairingClaim("ABCD2345", "https://new.example.com", null),
            Identity(),
            replaceExisting: true);

        Assert.Null(store.Get(ParentDataCache.DailyKey));
        Assert.Null(store.Get(ParentDataCache.NotificationsKey));
    }

    [Fact]
    public async Task Failed_replacement_keeps_the_existing_link_and_cache()
    {
        var (service, handler, tokens, state, settings, store) = CreateLinkedState(
            _ => HttpStubs.Json(
                HttpStatusCode.Gone,
                """{"error":{"code":"pairing_unavailable","message":"Mã ghép nối không hợp lệ, đã dùng hoặc đã hết hạn."}}"""),
            seedCache: true);

        var exception = await Assert.ThrowsAsync<PairingException>(
            () => service.ClaimAsync(
                new PairingClaim("ABCD2345", "https://new.example.com", null),
                Identity(),
                replaceExisting: true));

        Assert.Equal(PairingFailure.InvalidCode, exception.Failure);
        Assert.Single(handler.Requests);
        Assert.Equal("efm_old-token", tokens.Token);
        Assert.True(state.IsLinked);
        Assert.Equal(Guid.Parse(OldDeviceId), state.DeviceId);
        Assert.Equal("https://cloud.example.com", state.CloudBaseUrl);
        Assert.Equal("https://cloud.example.com", settings.CloudBaseUrl);
        Assert.NotNull(store.Get(ParentDataCache.DailyKey));
    }

    [Fact]
    public async Task Linked_app_refuses_claim_without_replace_confirmation()
    {
        var (service, handler, tokens, _, _, _) = CreateLinkedState(_ => Claimed());

        var exception = await Assert.ThrowsAsync<PairingException>(
            () => service.ClaimAsync(
                new PairingClaim("ABCD2345", "https://new.example.com", null),
                Identity(),
                replaceExisting: false));

        Assert.Equal(PairingFailure.Configuration, exception.Failure);
        Assert.Empty(handler.Requests);
        Assert.Equal("efm_old-token", tokens.Token);
    }

    [Fact]
    public async Task Unlink_revokes_device_and_clears_token_but_keeps_server()
    {
        var (service, handler, tokens, state, settings, _) = CreateLinkedState(
            _ => HttpStubs.Json(HttpStatusCode.OK, """{"ok":true}"""));

        await service.UnlinkAsync();

        var request = Assert.Single(handler.Requests);
        Assert.Equal(HttpMethod.Delete, request.Method);
        Assert.Null(tokens.Token);
        Assert.False(state.IsLinked);
        Assert.Null(state.DeviceId);
        Assert.Null(state.FamilyId);
        Assert.Null(settings.FamilyId);
        Assert.Equal("https://cloud.example.com", settings.CloudBaseUrl);
        Assert.Equal("https://cloud.example.com", state.CloudBaseUrl);
    }

    [Fact]
    public async Task Unlink_without_token_clears_local_state_without_network()
    {
        var (service, handler, tokens, state, _, _) = CreateLinkedState(
            _ => HttpStubs.Json(HttpStatusCode.OK, """{"ok":true}"""));
        await tokens.ClearAsync();

        await service.UnlinkAsync();

        Assert.Empty(handler.Requests);
        Assert.False(state.IsLinked);
        Assert.Null(state.DeviceId);
    }

    private static (PairingService Service, RecordingHttpMessageHandler Handler, MemoryTokenStore Tokens, AppState State, ParentSettingsStore Settings, FakeKeyValueStore Store)
        CreateService(
            Func<HttpRequestMessage, HttpResponseMessage> responder,
            string? token = null,
            bool allowInsecure = false,
            AppState? state = null)
    {
        var handler = new RecordingHttpMessageHandler(responder);
        var tokens = new MemoryTokenStore(token);
        var appState = state ?? new AppState();
        var store = new FakeKeyValueStore();
        var settings = new ParentSettingsStore(store);
        var service = new PairingService(
            new CloudApiClient(new RecordingHttpClientFactory(handler), tokens, appState),
            tokens,
            appState,
            settings,
            new ParentDataCache(store),
            allowInsecure);
        return (service, handler, tokens, appState, settings, store);
    }

    private static (PairingService Service, RecordingHttpMessageHandler Handler, MemoryTokenStore Tokens, AppState State, ParentSettingsStore Settings, FakeKeyValueStore Store)
        CreateLinkedState(
            Func<HttpRequestMessage, HttpResponseMessage> responder,
            bool seedCache = false)
    {
        var state = new AppState
        {
            IsLinked = true,
            CloudBaseUrl = "https://cloud.example.com",
            FamilyId = Guid.Parse("11111111-2222-3333-4444-555555555555"),
            DeviceId = Guid.Parse(OldDeviceId),
            TimeZone = "Asia/Ho_Chi_Minh",
            DesktopName = "Old PC",
        };
        var (service, handler, tokens, appState, settings, store) =
            CreateService(responder, token: "efm_old-token", state: state);

        settings.CloudBaseUrl = "https://cloud.example.com";
        settings.FamilyId = state.FamilyId;
        settings.DeviceId = state.DeviceId;
        settings.DesktopName = state.DesktopName;
        settings.TimeZoneId = state.TimeZone;

        if (seedCache)
        {
            store.Set(ParentDataCache.DailyKey, "{\"cached\":true}");
            store.Set(ParentDataCache.NotificationsKey, "{\"cached\":true}");
        }

        return (service, handler, tokens, appState, settings, store);
    }

    [Fact]
    public async Task Re_pairing_does_not_mutate_app_state_cloud_url_before_claim_succeeds()
    {
        string? observedBaseUrlDuringClaim = null;
        AppState? capturedState = null;
        var handler = new RecordingHttpMessageHandler(request =>
        {
            if (request.RequestUri!.AbsolutePath == "/v1/pairing/claim")
            {
                observedBaseUrlDuringClaim = capturedState?.CloudBaseUrl;
                return HttpStubs.Json(HttpStatusCode.BadRequest, "{\"error\":{\"code\":\"pairing_expired\"}}");
            }
            return HttpStubs.Json(HttpStatusCode.NoContent, string.Empty);
        });
        capturedState = new AppState
        {
            IsLinked = true,
            CloudBaseUrl = "https://old.example.com",
            FamilyId = Guid.NewGuid(),
            DeviceId = Guid.Parse(OldDeviceId),
        };
        var store = new FakeKeyValueStore();
        var settings = new ParentSettingsStore(store)
        {
            CloudBaseUrl = "https://old.example.com",
            FamilyId = capturedState.FamilyId,
            DeviceId = capturedState.DeviceId,
        };
        var tokens = new MemoryTokenStore("efm_old-token");
        var service = new PairingService(
            new CloudApiClient(new RecordingHttpClientFactory(handler), tokens, capturedState),
            tokens,
            capturedState,
            settings,
            new ParentDataCache(store));

        await Assert.ThrowsAsync<PairingException>(() => service.ClaimAsync(
            new PairingClaim("ABCD2345", "https://new.example.com", null),
            Identity(),
            replaceExisting: true));

        Assert.Equal("https://old.example.com", observedBaseUrlDuringClaim);
        Assert.Equal("https://old.example.com", capturedState.CloudBaseUrl);
    }

    [Fact]
    public async Task Unlink_clears_cache_and_local_link_even_when_token_store_read_throws()
    {
        var handler = new RecordingHttpMessageHandler(_ => HttpStubs.Json(HttpStatusCode.NoContent, string.Empty));
        var state = new AppState
        {
            IsLinked = true,
            CloudBaseUrl = "https://cloud.example.com",
            FamilyId = Guid.NewGuid(),
            DeviceId = Guid.Parse(OldDeviceId),
        };
        var store = new FakeKeyValueStore();
        store.Set(ParentDataCache.DailyKey, "{\"cached\":true}");
        store.Set(ParentDataCache.NotificationsKey, "{\"cached\":true}");
        var settings = new ParentSettingsStore(store)
        {
            CloudBaseUrl = "https://cloud.example.com",
            FamilyId = state.FamilyId,
            DeviceId = state.DeviceId,
        };
        var throwingTokens = new ThrowingReadTokenStore();
        var service = new PairingService(
            new CloudApiClient(new RecordingHttpClientFactory(handler), throwingTokens, state),
            throwingTokens,
            state,
            settings,
            new ParentDataCache(store));

        await service.UnlinkAsync();

        Assert.False(state.IsLinked);
        Assert.Null(settings.DeviceId);
        Assert.True(throwingTokens.Cleared);
        Assert.Null(store.Get(ParentDataCache.DailyKey));
        Assert.Null(store.Get(ParentDataCache.NotificationsKey));
    }

    [Fact]
    public async Task Re_pairing_clears_stale_push_token_and_app_version()
    {
        var (service, _, _, _, settings, _) = CreateLinkedState(_ => Claimed());
        settings.PushToken = "fcm-old-token";
        settings.PushAppVersion = "1.0.0";

        await service.ClaimAsync(
            new PairingClaim("ABCD2345", "https://new.example.com", null),
            Identity(),
            replaceExisting: true);

        Assert.Null(settings.PushToken);
        Assert.Null(settings.PushAppVersion);
    }

    private sealed class ThrowingReadTokenStore : ITokenStore
    {
        public bool Cleared { get; private set; }

        public Task<string?> GetTokenAsync(CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException("Keystore corrupted.");

        public Task SaveTokenAsync(string token, CancellationToken cancellationToken = default) =>
            Task.CompletedTask;

        public Task ClearAsync(CancellationToken cancellationToken = default)
        {
            Cleared = true;
            return Task.CompletedTask;
        }
    }

    private static MobileDeviceIdentity Identity() =>
        new("Pixel test", "android", "0.1.0", "Asia/Ho_Chi_Minh");

    private static HttpResponseMessage Claimed() =>
        HttpStubs.Json(HttpStatusCode.OK, $$"""
            {"mobileToken":"efm_new-token",
             "familyId":"{{NewFamilyId}}",
             "deviceId":"{{NewDeviceId}}",
             "desktopName":"Student PC",
             "timeZoneId":"Asia/Ho_Chi_Minh",
             "linkedAtUtc":"2026-11-17T08:00:00+00:00"}
            """);
}
