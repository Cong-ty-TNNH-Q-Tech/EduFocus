using System.Net;
using EduFocus.Parent.Core;
using EduFocus.Parent.Core.Pairing;
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
        var (service, handler, tokens, state, settings) = CreateService(_ => Claimed());

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
        var (service, handler, _, _, _) = CreateService(_ => Claimed());

        await service.ClaimAsync(
            new PairingClaim("ABCD2345", "https://cloud.example.com", SessionId: null),
            Identity(),
            replaceExisting: false);

        Assert.DoesNotContain("\"sessionId\"", handler.RequestBodies[0]);
    }

    [Fact]
    public async Task Invalid_code_is_rejected_before_any_network_call()
    {
        var (service, handler, tokens, state, _) = CreateService(_ => Claimed());

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
        var (service, handler, _, _, _) = CreateService(_ => Claimed());

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
        var (service, _, _, _, _) = CreateService(_ => Claimed());

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
        var (service, _, tokens, _, _) = CreateService(_ => HttpStubs.Json(
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
        var (service, _, _, _, _) = CreateService(_ => HttpStubs.RateLimited(
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
        var (service, _, _, _, _) = CreateService(_ => throw new HttpRequestException("offline"));

        var exception = await Assert.ThrowsAsync<PairingException>(
            () => service.ClaimAsync(
                new PairingClaim("ABCD2345", "https://cloud.example.com", null),
                Identity(),
                replaceExisting: false));

        Assert.Equal(PairingFailure.Network, exception.Failure);
        Assert.Contains("mạng", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Replace_revokes_old_device_before_claiming()
    {
        var (service, handler, tokens, state, _) = CreateLinkedState(responder: request =>
            request.Method == HttpMethod.Delete
                ? HttpStubs.Json(HttpStatusCode.OK, """{"ok":true}""")
                : Claimed());

        await service.ClaimAsync(
            new PairingClaim("ABCD2345", "https://new.example.com", null),
            Identity(),
            replaceExisting: true);

        Assert.Equal(2, handler.Requests.Count);
        var revoke = handler.Requests[0];
        Assert.Equal(HttpMethod.Delete, revoke.Method);
        Assert.Equal($"https://cloud.example.com/v1/devices/{OldDeviceId}", revoke.RequestUri?.ToString());
        Assert.Equal("efm_old-token", revoke.Headers.Authorization?.Parameter);

        Assert.Null(handler.Requests[1].Headers.Authorization);
        Assert.Equal("https://new.example.com/v1/pairing/claim", handler.Requests[1].RequestUri?.ToString());
        Assert.Equal("efm_new-token", tokens.Token);
        Assert.Equal(Guid.Parse(NewDeviceId), state.DeviceId);
        Assert.Equal("https://new.example.com", state.CloudBaseUrl);
    }

    [Fact]
    public async Task Replace_still_claims_when_old_token_revoke_fails()
    {
        var (service, handler, tokens, state, _) = CreateLinkedState(responder: request =>
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
    }

    [Fact]
    public async Task Linked_app_refuses_claim_without_replace_confirmation()
    {
        var (service, handler, tokens, _, _) = CreateLinkedState(_ => Claimed());

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
        var (service, handler, tokens, state, settings) = CreateLinkedState(
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
        var (service, handler, tokens, state, _) = CreateLinkedState(
            _ => HttpStubs.Json(HttpStatusCode.OK, """{"ok":true}"""));
        await tokens.ClearAsync();

        await service.UnlinkAsync();

        Assert.Empty(handler.Requests);
        Assert.False(state.IsLinked);
        Assert.Null(state.DeviceId);
    }

    private static (PairingService Service, RecordingHttpMessageHandler Handler, MemoryTokenStore Tokens, AppState State, ParentSettingsStore Settings)
        CreateService(
            Func<HttpRequestMessage, HttpResponseMessage> responder,
            string? token = null,
            bool allowInsecure = false,
            AppState? state = null)
    {
        var handler = new RecordingHttpMessageHandler(responder);
        var tokens = new MemoryTokenStore(token);
        var appState = state ?? new AppState();
        var settings = new ParentSettingsStore(new FakeKeyValueStore());
        var service = new PairingService(
            new CloudApiClient(new RecordingHttpClientFactory(handler), tokens, appState),
            tokens,
            appState,
            settings,
            allowInsecure);
        return (service, handler, tokens, appState, settings);
    }

    private static (PairingService Service, RecordingHttpMessageHandler Handler, MemoryTokenStore Tokens, AppState State, ParentSettingsStore Settings)
        CreateLinkedState(Func<HttpRequestMessage, HttpResponseMessage> responder)
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
        var (service, handler, tokens, appState, settings) =
            CreateService(responder, token: "efm_old-token", state: state);

        settings.CloudBaseUrl = "https://cloud.example.com";
        settings.FamilyId = state.FamilyId;
        settings.DeviceId = state.DeviceId;
        settings.DesktopName = state.DesktopName;
        settings.TimeZoneId = state.TimeZone;

        return (service, handler, tokens, appState, settings);
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
