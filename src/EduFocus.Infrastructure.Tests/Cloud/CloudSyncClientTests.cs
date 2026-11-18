using System.Net;
using System.Text;
using System.Text.Json;
using EduFocus.Infrastructure.Cloud;
using EduFocus.Ports.Outbound.Cloud;
using FluentAssertions;

namespace EduFocus.Infrastructure.Tests.Cloud;

public sealed class CloudSyncClientTests
{
    private const string Token = "efd_desktop_token_must_never_leak";
    private const string CloudBaseUrl = "https://cloud.example.com";

    private static readonly Guid SessionId = Guid.Parse("0f8fad5b-d9cb-469f-a165-70867728950e");
    private static readonly Guid FamilyId = Guid.Parse("7c9e6679-7425-40de-944b-e07fc1f90ae7");
    private static readonly Guid DeviceId = Guid.Parse("3f2504e0-4f89-41d3-9a0c-0305e82c3301");
    private static readonly Guid MobileDeviceId = Guid.Parse("9b2d4a3e-6c1f-4d5a-8b7c-2e1f0a9d8c7b");

    [Fact]
    public async Task Pairing_session_posts_the_request_and_returns_the_desktop_token()
    {
        var handler = new StubHandler(_ => JsonResponse(
            HttpStatusCode.OK,
            new
            {
                sessionId = SessionId,
                code = "ABCD2345",
                pairingUri = $"edufocus://pair?v=1&u=Y2xvdWQ&s={SessionId:D}&c=ABCD2345",
                expiresAtUtc = "2026-11-17T09:10:00Z",
                desktopToken = Token,
                cloudBaseUrl = CloudBaseUrl
            }));

        var client = new CloudSyncClient(new HttpClient(handler), "1.2.3");

        var session = await client.CreatePairingSessionAsync(
            new Uri(CloudBaseUrl),
            new CloudPairingRequest("PC của An", "windows", "1.2.3", "Asia/Ho_Chi_Minh"));

        session.SessionId.Should().Be(SessionId);
        session.Code.Should().Be("ABCD2345");
        session.DesktopToken.Should().Be(Token);
        session.ExpiresAtUtc.Kind.Should().Be(DateTimeKind.Utc);
        session.ExpiresAtUtc.Should().Be(new DateTime(2026, 11, 17, 9, 10, 0, DateTimeKind.Utc));

        var request = handler.Requests.Should().ContainSingle().Subject;
        request.Method.Should().Be("POST");
        request.Uri!.AbsolutePath.Should().Be("/v1/pairing/sessions");
        request.Authorization.Should().BeNull();
        JsonProperty(request.Body, "desktopName").Should().Be("PC của An");
        JsonProperty(request.Body, "platform").Should().Be("windows");
        JsonProperty(request.Body, "timeZoneId").Should().Be("Asia/Ho_Chi_Minh");
    }

    [Fact]
    public async Task Link_status_sends_the_bearer_token_and_maps_devices()
    {
        var handler = new StubHandler(_ => JsonResponse(
            HttpStatusCode.OK,
            new
            {
                linked = true,
                familyId = FamilyId,
                deviceId = DeviceId,
                timeZoneId = "Asia/Ho_Chi_Minh",
                linkedAtUtc = "2026-11-17T09:00:00Z",
                lastSyncAtUtc = "2026-11-17T09:00:30Z",
                pendingSyncEvents = 2,
                devices = new[]
                {
                    new
                    {
                        id = DeviceId,
                        kind = "Desktop",
                        name = "PC của An",
                        platform = "windows",
                        createdAtUtc = "2026-11-17T09:00:00Z",
                        lastSeenAtUtc = "2026-11-17T09:00:30Z",
                        revokedAtUtc = (string?)null
                    },
                    new
                    {
                        id = MobileDeviceId,
                        kind = "Mobile",
                        name = "Điện thoại của mẹ",
                        platform = "android",
                        createdAtUtc = "2026-11-17T09:00:20Z",
                        lastSeenAtUtc = "2026-11-17T09:00:30Z",
                        revokedAtUtc = (string?)"2026-11-17T09:01:00Z"
                    }
                }
            }));

        var client = new CloudSyncClient(new HttpClient(handler));

        var status = await client.GetLinkStatusAsync(new Uri(CloudBaseUrl), Token);

        status.Linked.Should().BeTrue();
        status.FamilyId.Should().Be(FamilyId);
        status.DeviceId.Should().Be(DeviceId);
        status.LastSyncAtUtc.Should().Be(new DateTime(2026, 11, 17, 9, 0, 30, DateTimeKind.Utc));
        status.PendingSyncEvents.Should().Be(2);
        status.Devices.Should().HaveCount(2);
        status.Devices[0].IsRevoked.Should().BeFalse();
        status.Devices[1].IsRevoked.Should().BeTrue();

        var request = handler.Requests.Should().ContainSingle().Subject;
        request.Uri!.AbsolutePath.Should().Be("/v1/link/status");
        request.Authorization.Should().Be($"Bearer {Token}");
    }

    [Fact]
    public async Task Pairing_status_maps_the_claimed_state()
    {
        var handler = new StubHandler(_ => JsonResponse(
            HttpStatusCode.OK,
            new
            {
                sessionId = SessionId,
                status = "claimed",
                expiresAtUtc = "2026-11-17T09:10:00Z",
                claimedAtUtc = "2026-11-17T09:00:20Z",
                mobileDeviceName = "Điện thoại của mẹ",
                mobileDeviceId = MobileDeviceId
            }));

        var client = new CloudSyncClient(new HttpClient(handler));

        var status = await client.GetPairingSessionStatusAsync(new Uri(CloudBaseUrl), SessionId, Token);

        status.Status.Should().Be(CloudPairingStatus.Claimed);
        status.ClaimedAtUtc.Should().Be(new DateTime(2026, 11, 17, 9, 0, 20, DateTimeKind.Utc));
        status.MobileDeviceName.Should().Be("Điện thoại của mẹ");
        status.MobileDeviceId.Should().Be(MobileDeviceId);
        handler.Requests[0].Uri!.AbsolutePath.Should().Be($"/v1/pairing/sessions/{SessionId:D}");
    }

    [Fact]
    public async Task Unauthorized_errors_never_include_the_token()
    {
        var handler = new StubHandler(_ => JsonResponse(
            HttpStatusCode.Unauthorized,
            new
            {
                error = new
                {
                    code = "unauthorized",
                    message = "Token không hợp lệ hoặc đã bị thu hồi."
                }
            }));

        var client = new CloudSyncClient(new HttpClient(handler));

        var act = async () => await client.GetLinkStatusAsync(new Uri(CloudBaseUrl), Token);
        var exception = (await act.Should().ThrowAsync<CloudSyncException>()).Which;

        exception.Code.Should().Be(CloudErrorCodes.Unauthorized);
        exception.StatusCode.Should().Be(401);
        exception.Message.Should().Be("Token không hợp lệ hoặc đã bị thu hồi.");
        exception.ToString().Should().NotContain(Token);
    }

    [Fact]
    public async Task Network_failures_map_to_a_safe_network_error()
    {
        var handler = new StubHandler(_ => throw new HttpRequestException("connection refused"));

        var client = new CloudSyncClient(new HttpClient(handler));

        var act = async () => await client.GetLinkStatusAsync(new Uri(CloudBaseUrl), Token);
        var exception = (await act.Should().ThrowAsync<CloudSyncException>()).Which;

        exception.Code.Should().Be(CloudErrorCodes.Network);
        exception.IsNetworkError.Should().BeTrue();
        exception.ToString().Should().NotContain(Token);
        exception.InnerException.Should().BeNull();
    }

    [Fact]
    public async Task Malformed_success_bodies_map_to_invalid_response()
    {
        var handler = new StubHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent("<html>not json</html>", Encoding.UTF8, "text/html")
        });

        var client = new CloudSyncClient(new HttpClient(handler));

        var act = async () => await client.GetLinkStatusAsync(new Uri(CloudBaseUrl), Token);
        var exception = (await act.Should().ThrowAsync<CloudSyncException>()).Which;

        exception.Code.Should().Be(CloudErrorCodes.InvalidResponse);
        exception.ToString().Should().NotContain(Token);
    }

    [Fact]
    public async Task Delete_family_sends_the_confirmation_text()
    {
        var handler = new StubHandler(_ => JsonResponse(HttpStatusCode.OK, new { success = true }));

        var client = new CloudSyncClient(new HttpClient(handler));

        await client.DeleteFamilyAsync(new Uri(CloudBaseUrl), Token);

        var request = handler.Requests.Should().ContainSingle().Subject;
        request.Method.Should().Be("DELETE");
        request.Uri!.AbsolutePath.Should().Be("/v1/family");
        JsonProperty(request.Body, "confirmation").Should().Be("XÓA");
        request.Authorization.Should().Be($"Bearer {Token}");
    }

    [Fact]
    public async Task Revoke_device_uses_the_device_route()
    {
        var handler = new StubHandler(_ => JsonResponse(HttpStatusCode.OK, new { success = true }));

        var client = new CloudSyncClient(new HttpClient(handler));

        await client.RevokeDeviceAsync(new Uri(CloudBaseUrl), Token, DeviceId);

        var request = handler.Requests.Should().ContainSingle().Subject;
        request.Method.Should().Be("DELETE");
        request.Uri!.AbsolutePath.Should().Be($"/v1/devices/{DeviceId:D}");
        request.Authorization.Should().Be($"Bearer {Token}");
    }

    [Fact]
    public async Task Client_sends_user_agent_per_request_without_mutating_shared_http_client()
    {
        var handler = new StubHandler(_ => JsonResponse(
            HttpStatusCode.OK,
            new { linked = true, devices = Array.Empty<object>(), pendingSyncEvents = 0 }));
        var http = new HttpClient(handler);
        await http.GetAsync(new Uri($"{CloudBaseUrl}/health"));

        var client = new CloudSyncClient(http, "1.2.3");
        await client.GetLinkStatusAsync(new Uri(CloudBaseUrl), Token);

        http.DefaultRequestHeaders.UserAgent.Should().BeEmpty();
        handler.Requests[^1].UserAgent.Should().Be("EduFocus/1.2.3");
    }

    [Fact]
    public async Task Send_events_posts_the_batch_and_maps_the_results()
    {
        var firstId = Guid.Parse("c2b3f0f0-0f54-4d1a-8a4f-4a1f1f2e6b11");
        var secondId = Guid.Parse("8a6c2d1e-7f2b-4a9e-9d5c-3b1a2f4e6d22");
        var handler = new StubHandler(_ => JsonResponse(
            HttpStatusCode.OK,
            new
            {
                results = new object[]
                {
                    new { clientEventId = firstId.ToString("D"), status = "applied" },
                    new
                    {
                        clientEventId = secondId.ToString("D"),
                        status = "rejected",
                        code = "stale_summary",
                        message = "Bản tóm tắt đã quá cũ."
                    }
                },
                serverTimeUtc = "2026-11-17T09:00:30Z"
            }));

        var client = new CloudSyncClient(new HttpClient(handler));
        var occurredAt = new DateTimeOffset(2026, 11, 17, 9, 0, 0, TimeSpan.Zero);

        var result = await client.SendEventsAsync(
            new Uri(CloudBaseUrl),
            Token,
            [
                new CloudSyncEvent(
                    firstId.ToString("D"),
                    CloudEventTypes.DailySummary,
                    occurredAt,
                    JsonSerializer.SerializeToElement(new { sessionsCount = 1 })),
                new CloudSyncEvent(
                    secondId.ToString("D"),
                    CloudEventTypes.AttentionAlert,
                    occurredAt,
                    JsonSerializer.SerializeToElement(new { kind = "distracted" }))
            ]);

        result.Results.Should().HaveCount(2);
        result.Results[0].Outcome.Should().Be(CloudEventOutcome.Applied);
        result.Results[1].Outcome.Should().Be(CloudEventOutcome.Rejected);
        result.Results[1].Code.Should().Be("stale_summary");
        result.ServerTimeUtc.Should().Be(occurredAt.AddSeconds(30));

        var request = handler.Requests.Should().ContainSingle().Subject;
        request.Method.Should().Be("POST");
        request.Uri!.AbsolutePath.Should().Be("/v1/sync/events");
        request.Authorization.Should().Be($"Bearer {Token}");

        using var body = JsonDocument.Parse(request.Body);
        var events = body.RootElement.GetProperty("events");
        events.GetArrayLength().Should().Be(2);
        events[0].GetProperty("clientEventId").GetString().Should().Be(firstId.ToString("D"));
        events[0].GetProperty("type").GetString().Should().Be("daily_summary");
        events[0].GetProperty("occurredAtUtc").GetString().Should().Be("2026-11-17T09:00:00+00:00");
        events[0].GetProperty("payload").GetProperty("sessionsCount").GetInt32().Should().Be(1);
    }

    [Fact]
    public async Task Rate_limited_errors_expose_the_retry_after_delay()
    {
        var handler = new StubHandler(_ =>
        {
            var response = new HttpResponseMessage(HttpStatusCode.TooManyRequests)
            {
                Content = new StringContent(string.Empty, Encoding.UTF8, "application/json")
            };
            response.Headers.RetryAfter =
                new System.Net.Http.Headers.RetryConditionHeaderValue(TimeSpan.FromMinutes(3));
            return response;
        });

        var client = new CloudSyncClient(new HttpClient(handler));

        var act = async () => await client.GetLinkStatusAsync(new Uri(CloudBaseUrl), Token);
        var exception = (await act.Should().ThrowAsync<CloudSyncException>()).Which;

        exception.Code.Should().Be(CloudErrorCodes.RateLimited);
        exception.StatusCode.Should().Be(429);
        exception.RetryAfter.Should().Be(TimeSpan.FromMinutes(3));
        exception.ToString().Should().NotContain(Token);
    }

    private static string JsonProperty(string body, string name) =>
        JsonDocument.Parse(body).RootElement.GetProperty(name).GetString()!;

    private static HttpResponseMessage JsonResponse(HttpStatusCode statusCode, object payload) =>
        new(statusCode)
        {
            Content = new StringContent(
                JsonSerializer.Serialize(payload),
                Encoding.UTF8,
                "application/json")
        };

    private sealed class StubHandler : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, HttpResponseMessage> _respond;

        public StubHandler(Func<HttpRequestMessage, HttpResponseMessage> respond) => _respond = respond;

        public List<CapturedRequest> Requests { get; } = [];

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            var body = request.Content is null
                ? string.Empty
                : await request.Content.ReadAsStringAsync(cancellationToken);

            Requests.Add(new CapturedRequest(
                request.Method.Method,
                request.RequestUri,
                request.Headers.Authorization?.ToString(),
                body,
                request.Headers.UserAgent.ToString()));

            return _respond(request);
        }
    }

    private sealed record CapturedRequest(
        string Method,
        Uri? Uri,
        string? Authorization,
        string Body,
        string UserAgent);
}
