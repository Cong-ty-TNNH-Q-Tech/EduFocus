using System.Net;
using System.Text.Json;
using EduFocus.Cloud.Contracts.Family;
using EduFocus.Cloud.Contracts.Json;
using EduFocus.Parent.Core;
using EduFocus.Parent.Core.Presentation;
using EduFocus.Parent.Core.Services;
using EduFocus.Parent.Core.Settings;
using EduFocus.Parent.Tests.TestSupport;

namespace EduFocus.Parent.Tests.Presentation;

public sealed class FamilyAdminServiceTests
{
    [Fact]
    public async Task Delete_family_sends_confirmation_and_clears_local_link()
    {
        var harness = new Harness(_ => HttpStubs.Json(HttpStatusCode.OK, """{"ok":true}"""));

        await harness.Service.DeleteFamilyAsync(FamilyAdminService.DeleteConfirmation);

        var request = Assert.Single(harness.Handler.Requests);
        Assert.Equal(HttpMethod.Delete, request.Method);
        Assert.Equal("https://cloud.example.com/v1/family", request.RequestUri?.ToString());
        var body = JsonSerializer.Deserialize<DeleteFamilyRequest>(
            harness.Handler.RequestBodies[0],
            ApiJson.Options);
        Assert.Equal("XÓA", body?.Confirmation);

        Assert.Null(harness.Tokens.Token);
        Assert.False(harness.State.IsLinked);
        Assert.Null(harness.Settings.FamilyId);
    }

    [Fact]
    public async Task Wrong_confirmation_is_rejected_before_network()
    {
        var harness = new Harness(_ => HttpStubs.Json(HttpStatusCode.OK, "{}"));

        await Assert.ThrowsAsync<ArgumentException>(
            () => harness.Service.DeleteFamilyAsync("xoa"));

        Assert.Empty(harness.Handler.Requests);
        Assert.NotNull(harness.Tokens.Token);
    }

    [Fact]
    public async Task Revoke_device_uses_device_path()
    {
        var deviceId = Guid.NewGuid();
        var harness = new Harness(_ => HttpStubs.Json(HttpStatusCode.OK, """{"ok":true}"""));

        await harness.Service.RevokeDeviceAsync(deviceId);

        var request = Assert.Single(harness.Handler.Requests);
        Assert.Equal(HttpMethod.Delete, request.Method);
        Assert.Equal(
            $"https://cloud.example.com/v1/devices/{deviceId:D}",
            request.RequestUri?.ToString());
    }

    [Fact]
    public async Task Get_devices_returns_family_devices()
    {
        var deviceId = Guid.NewGuid();
        var harness = new Harness(_ => HttpStubs.Json(
            HttpStatusCode.OK,
            $$"""
            {"devices":[{"id":"{{deviceId:D}}","kind":"Mobile","name":"Pixel",
              "platform":"android","createdAtUtc":"2026-11-17T08:00:00+00:00",
              "lastSeenAtUtc":null,"revokedAtUtc":null}]}
            """));

        var devices = await harness.Service.GetDevicesAsync();

        var device = Assert.Single(devices);
        Assert.Equal(deviceId, device.Id);
        Assert.Equal("Pixel", device.Name);
        Assert.Equal("/v1/devices", harness.Handler.Requests[0].RequestUri?.AbsolutePath);
    }

    private sealed class Harness
    {
        public Harness(Func<HttpRequestMessage, HttpResponseMessage> responder)
        {
            State = new AppState
            {
                CloudBaseUrl = "https://cloud.example.com",
                IsLinked = true,
                FamilyId = Guid.NewGuid(),
                DeviceId = Guid.NewGuid(),
            };
            var keyValue = new FakeKeyValueStore();
            Settings = new ParentSettingsStore(keyValue)
            {
                FamilyId = State.FamilyId,
                DeviceId = State.DeviceId,
                DesktopName = "Student PC",
                TimeZoneId = "Asia/Ho_Chi_Minh",
            };
            Tokens = new MemoryTokenStore("efm_token");
            var cache = new ParentDataCache(keyValue);
            var session = new SessionExpiryService(Tokens, State, Settings, cache);
            var api = new CloudApiClient(
                new RecordingHttpClientFactory(Handler = new RecordingHttpMessageHandler(responder)),
                Tokens,
                State);
            Service = new FamilyAdminService(api, session);
        }

        public RecordingHttpMessageHandler Handler { get; }

        public MemoryTokenStore Tokens { get; }

        public ParentSettingsStore Settings { get; }

        public AppState State { get; }

        public FamilyAdminService Service { get; }
    }
}
