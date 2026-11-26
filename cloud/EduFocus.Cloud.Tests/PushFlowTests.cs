using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Net.WebSockets;
using System.Text.Json;
using System.Text.Json.Serialization;
using EduFocus.Cloud.Api.Push;
using EduFocus.Cloud.Contracts.Common;
using EduFocus.Cloud.Contracts.Pairing;
using EduFocus.Cloud.Contracts.Preferences;
using EduFocus.Cloud.Contracts.Push;
using EduFocus.Cloud.Contracts.Sync;
using EduFocus.Cloud.Data;
using EduFocus.Cloud.Data.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using SyncEventType = EduFocus.Cloud.Contracts.Sync.SyncEventType;

namespace EduFocus.Cloud.Tests;

[Collection("CloudDatabase")]
public sealed class PushFlowTests
{
    private const string TestDbVariable = "EDUFOCUS_CLOUD_TEST_DB";
    private const string TimeZone = "Asia/Ho_Chi_Minh";
    private const string Endpoint1 = "https://push.example.com/sub/device-1";
    private const string Endpoint2 = "https://push.example.com/sub/device-2";
    private const string P256dh = "BAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA";
    private const string Auth = "AAAAAAAAAAAAAAAAAAAAAA";

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) },
    };

    [Fact]
    public async Task Paired_mobile_can_read_push_config()
    {
        var connectionString = TestConnectionString();
        if (connectionString is null)
        {
            return;
        }

        await ResetDatabaseAsync(connectionString);
        await using var factory = new CloudApiFactory(connectionString);
        using var anonymous = factory.CreateClient();
        var paired = await PairAsync(anonymous);
        using var mobile = CreateClient(factory, paired.MobileToken);

        var config = await mobile.GetFromJsonAsync<PushConfigResponse>(
            "/v1/push/config",
            Json);

        Assert.NotNull(config);
        Assert.False(config.Enabled);
        Assert.Null(config.VapidPublicKey);
        Assert.False(config.WebSocketEnabled);
    }

    [Fact]
    public async Task Push_config_reports_websocket_transport()
    {
        var connectionString = TestConnectionString();
        if (connectionString is null)
        {
            return;
        }

        await ResetDatabaseAsync(connectionString);
        await using var factory = new CloudApiFactory(
            connectionString,
            new Dictionary<string, string> { ["Push:WebSocketEnabled"] = "true" });
        using var anonymous = factory.CreateClient();
        var paired = await PairAsync(anonymous);
        using var mobile = CreateClient(factory, paired.MobileToken);

        var config = await mobile.GetFromJsonAsync<PushConfigResponse>(
            "/v1/push/config",
            Json);

        Assert.NotNull(config);
        Assert.False(config.Enabled);
        Assert.Null(config.VapidPublicKey);
        Assert.True(config.WebSocketEnabled);
    }

    [Fact]
    public async Task Websocket_stream_receives_pending_alert()
    {
        var connectionString = TestConnectionString();
        if (connectionString is null)
        {
            return;
        }

        await ResetDatabaseAsync(connectionString);
        var fake = new FakePushSender();
        await using var factory = CreateFactory(
            connectionString,
            fake,
            new Dictionary<string, string> { ["Push:WebSocketEnabled"] = "true" },
            services => services.RemoveAll<IHostedService>());
        using var anonymous = factory.CreateClient();
        var paired = await PairAsync(anonymous);
        using var desktop = CreateClient(factory, paired.DesktopToken);
        using var mobile = CreateClient(factory, paired.MobileToken);
        await PutPreferencesAsync(mobile, Preferences(quietStart: 0, quietEnd: 0));

        var webSocketClient = factory.Server.CreateWebSocketClient();
        webSocketClient.ConfigureRequest = request =>
            request.Headers.Authorization = $"Bearer {paired.MobileToken}";
        using var socket = await webSocketClient.ConnectAsync(
            new Uri("ws://localhost/v1/push/stream"),
            CancellationToken.None);

        var registry = factory.Services.GetRequiredService<PushStreamRegistry>();
        await WaitUntilAsync(() => registry.Count == 1, TimeSpan.FromSeconds(3));

        await IngestAsync(desktop, AttentionAlert("alert-ws", DateTimeOffset.UtcNow));
        await DispatchAsync(factory);

        var message = await ReceiveJsonAsync(socket, TimeSpan.FromSeconds(5));
        var notificationId = message.GetProperty("notificationId").GetString();
        Assert.Equal("attention_alert", message.GetProperty("type").GetString());
        Assert.Equal(LocalToday().ToString("yyyy-MM-dd"), message.GetProperty("date").GetString());
        Assert.NotNull(notificationId);

        await using var db = CreateContext(connectionString);
        var notification = await db.Notifications.AsNoTracking().SingleAsync();
        Assert.Equal(NotificationStatus.Sent, notification.Status);
        Assert.Equal(notification.Id.ToString(), notificationId);
        Assert.Equal(1, notification.AttemptCount);
        Assert.Equal(0, fake.Calls);
    }

    [Fact]
    public async Task Revoking_mobile_device_closes_its_websocket_stream()
    {
        var connectionString = TestConnectionString();
        if (connectionString is null)
        {
            return;
        }

        await ResetDatabaseAsync(connectionString);
        await using var factory = CreateFactory(
            connectionString,
            new FakePushSender(),
            new Dictionary<string, string> { ["Push:WebSocketEnabled"] = "true" },
            services => services.RemoveAll<IHostedService>());
        using var anonymous = factory.CreateClient();
        var paired = await PairAsync(anonymous);
        using var desktop = CreateClient(factory, paired.DesktopToken);

        var webSocketClient = factory.Server.CreateWebSocketClient();
        webSocketClient.ConfigureRequest = request =>
            request.Headers.Authorization = $"Bearer {paired.MobileToken}";
        using var socket = await webSocketClient.ConnectAsync(
            new Uri("ws://localhost/v1/push/stream"),
            CancellationToken.None);

        var registry = factory.Services.GetRequiredService<PushStreamRegistry>();
        await WaitUntilAsync(() => registry.Count == 1, TimeSpan.FromSeconds(3));

        Guid mobileDeviceId;
        await using (var db = CreateContext(connectionString))
        {
            mobileDeviceId = await db.Devices
                .Where(d => d.Kind == DeviceKind.Mobile)
                .Select(d => d.Id)
                .SingleAsync();
        }

        var revoke = await desktop.DeleteAsync($"/v1/devices/{mobileDeviceId:D}");
        Assert.Equal(HttpStatusCode.OK, revoke.StatusCode);
        await WaitUntilAsync(() => registry.Count == 0, TimeSpan.FromSeconds(3));
    }

    [Fact]
    public async Task Websocket_enabled_keeps_alert_pending_until_mobile_connects()
    {
        var connectionString = TestConnectionString();
        if (connectionString is null)
        {
            return;
        }

        await ResetDatabaseAsync(connectionString);
        var fake = new FakePushSender();
        await using var factory = CreateFactory(
            connectionString,
            fake,
            new Dictionary<string, string> { ["Push:WebSocketEnabled"] = "true" },
            services => services.RemoveAll<IHostedService>());
        using var anonymous = factory.CreateClient();
        var paired = await PairAsync(anonymous);
        using var desktop = CreateClient(factory, paired.DesktopToken);
        using var mobile = CreateClient(factory, paired.MobileToken);
        await PutPreferencesAsync(mobile, Preferences(quietStart: 0, quietEnd: 0));

        await IngestAsync(desktop, SecurityAlert("security-ws", DateTimeOffset.UtcNow));
        await DispatchAsync(factory);

        await using var db = CreateContext(connectionString);
        var notification = await db.Notifications.AsNoTracking().SingleAsync();
        Assert.Equal(NotificationStatus.Pending, notification.Status);
        Assert.Equal("no_transport", notification.FailureCode);
        Assert.Equal(0, notification.AttemptCount);
        Assert.Equal(0, fake.Calls);
    }

    [Fact]
    public async Task Alert_is_pushed_as_data_only_message_and_marked_sent()
    {
        var connectionString = TestConnectionString();
        if (connectionString is null)
        {
            return;
        }

        await ResetDatabaseAsync(connectionString);
        var fake = new FakePushSender();
        await using var factory = CreateFactory(connectionString, fake);
        using var anonymous = factory.CreateClient();
        var paired = await PairAsync(anonymous);
        using var desktop = CreateClient(factory, paired.DesktopToken);
        using var mobile = CreateClient(factory, paired.MobileToken);
        await PutPreferencesAsync(mobile, Preferences(quietStart: 0, quietEnd: 0));

        var register = await mobile.PostAsJsonAsync(
            "/v1/push/registrations",
            RegisterRequest());
        Assert.Equal(HttpStatusCode.OK, register.StatusCode);

        var occurredAt = DateTimeOffset.UtcNow;
        var results = await IngestAsync(desktop, AttentionAlert("alert-1", occurredAt));
        Assert.Equal(SyncEventResultStatus.Applied, results[0].Status);

        await DispatchAsync(factory);

        await using var db = CreateContext(connectionString);
        var notification = await db.Notifications.AsNoTracking().SingleAsync();
        Assert.Equal(NotificationStatus.Sent, notification.Status);
        Assert.NotNull(notification.SentAtUtc);
        Assert.Equal(1, notification.AttemptCount);
        Assert.Equal(LocalToday(), notification.LocalDate);
        Assert.StartsWith("attention_alert:distracted:", notification.DedupeKey);

        var syncEvent = await db.SyncEvents.AsNoTracking().SingleAsync();
        Assert.Equal(SyncEventStatus.Processed, syncEvent.Status);

        var registration = await db.PushRegistrations.AsNoTracking().SingleAsync();
        Assert.Null(registration.InvalidatedAtUtc);
        Assert.Equal(Endpoint1, registration.Endpoint);

        var message = Assert.Single(fake.Messages);
        var data = Assert.IsAssignableFrom<IReadOnlyDictionary<string, string>>(fake.Data);
        Assert.Equal(Endpoint1, message.Endpoint);
        Assert.Equal(3, data.Count);
        Assert.Equal("attention_alert", data["type"]);
        Assert.Equal(LocalToday().ToString("yyyy-MM-dd"), data["date"]);
        Assert.Equal(notification.Id.ToString(), data["notificationId"]);
    }

    [Fact]
    public async Task Disabled_preference_suppresses_notification()
    {
        var connectionString = TestConnectionString();
        if (connectionString is null)
        {
            return;
        }

        await ResetDatabaseAsync(connectionString);
        var fake = new FakePushSender();
        await using var factory = CreateFactory(connectionString, fake);
        using var anonymous = factory.CreateClient();
        var paired = await PairAsync(anonymous);
        using var desktop = CreateClient(factory, paired.DesktopToken);
        using var mobile = CreateClient(factory, paired.MobileToken);
        await PutPreferencesAsync(
            mobile,
            Preferences(quietStart: 0, quietEnd: 0, attentionAlert: false));
        await RegisterAsync(mobile);

        await IngestAsync(desktop, AttentionAlert("alert-1", DateTimeOffset.UtcNow));
        await DispatchAsync(factory);

        await using var db = CreateContext(connectionString);
        var notification = await db.Notifications.AsNoTracking().SingleAsync();
        Assert.Equal(NotificationStatus.Suppressed, notification.Status);
        Assert.Equal("preference_disabled", notification.FailureCode);
        Assert.Equal(0, fake.Calls);
    }

    [Fact]
    public async Task Quiet_hours_suppress_alerts_but_security_alerts_bypass()
    {
        var connectionString = TestConnectionString();
        if (connectionString is null)
        {
            return;
        }

        await ResetDatabaseAsync(connectionString);
        var fake = new FakePushSender();
        await using var factory = CreateFactory(connectionString, fake);
        using var anonymous = factory.CreateClient();
        var paired = await PairAsync(anonymous);
        using var desktop = CreateClient(factory, paired.DesktopToken);
        using var mobile = CreateClient(factory, paired.MobileToken);

        var localMinutes = PushRules.LocalMinutes(DateTimeOffset.UtcNow, TimeZone);
        var quietStart = (localMinutes + 1439) % 1440;
        var quietEnd = (localMinutes + 2) % 1440;
        await PutPreferencesAsync(mobile, Preferences(quietStart, quietEnd));
        await RegisterAsync(mobile);

        var now = DateTimeOffset.UtcNow;
        await IngestAsync(
            desktop,
            AttentionAlert("alert-1", now),
            SecurityAlert("security-1", now));
        await DispatchAsync(factory);

        await using var db = CreateContext(connectionString);
        var attention = await db.Notifications.AsNoTracking()
            .SingleAsync(n => n.Category == NotificationCategory.AttentionAlert);
        Assert.Equal(NotificationStatus.Suppressed, attention.Status);
        Assert.Equal("quiet_hours", attention.FailureCode);

        var security = await db.Notifications.AsNoTracking()
            .SingleAsync(n => n.Category == NotificationCategory.SecurityAlert);
        Assert.Equal(NotificationStatus.Sent, security.Status);

        Assert.Single(fake.Messages);
        Assert.Equal("security_alert", fake.Data!["type"]);
    }

    [Fact]
    public async Task Duplicate_alerts_within_window_create_single_notification()
    {
        var connectionString = TestConnectionString();
        if (connectionString is null)
        {
            return;
        }

        await ResetDatabaseAsync(connectionString);
        var fake = new FakePushSender();
        await using var factory = CreateFactory(connectionString, fake);
        using var anonymous = factory.CreateClient();
        var paired = await PairAsync(anonymous);
        using var desktop = CreateClient(factory, paired.DesktopToken);
        using var mobile = CreateClient(factory, paired.MobileToken);
        await PutPreferencesAsync(mobile, Preferences(quietStart: 0, quietEnd: 0));

        var windowStart = FloorToQuarterHour(DateTimeOffset.UtcNow.AddMinutes(-30));
        var results = await IngestAsync(
            desktop,
            AttentionAlert("alert-1", windowStart.AddMinutes(1)),
            AttentionAlert("alert-2", windowStart.AddMinutes(2)));
        Assert.All(results, result => Assert.Equal(SyncEventResultStatus.Applied, result.Status));

        await using var db = CreateContext(connectionString);
        var notification = await db.Notifications.AsNoTracking().SingleAsync();
        Assert.Equal(NotificationCategory.AttentionAlert, notification.Category);
        Assert.Equal(2, await db.SyncEvents.CountAsync());
    }

    [Fact]
    public async Task Family_without_mobile_registration_is_suppressed()
    {
        var connectionString = TestConnectionString();
        if (connectionString is null)
        {
            return;
        }

        await ResetDatabaseAsync(connectionString);
        var fake = new FakePushSender();
        await using var factory = CreateFactory(connectionString, fake);
        using var anonymous = factory.CreateClient();
        var paired = await PairAsync(anonymous);
        using var desktop = CreateClient(factory, paired.DesktopToken);

        await IngestAsync(desktop, SecurityAlert("security-1", DateTimeOffset.UtcNow));
        await DispatchAsync(factory);

        await using var db = CreateContext(connectionString);
        var notification = await db.Notifications.AsNoTracking().SingleAsync();
        Assert.Equal(NotificationStatus.Suppressed, notification.Status);
        Assert.Equal("no_device", notification.FailureCode);
        Assert.Equal(0, fake.Calls);
    }

    [Fact]
    public async Task Invalid_endpoint_is_invalidated_and_not_retried()
    {
        var connectionString = TestConnectionString();
        if (connectionString is null)
        {
            return;
        }

        await ResetDatabaseAsync(connectionString);
        var fake = new FakePushSender();
        fake.FailInvalid();
        await using var factory = CreateFactory(connectionString, fake);
        using var anonymous = factory.CreateClient();
        var paired = await PairAsync(anonymous);
        using var desktop = CreateClient(factory, paired.DesktopToken);
        using var mobile = CreateClient(factory, paired.MobileToken);
        await PutPreferencesAsync(mobile, Preferences(quietStart: 0, quietEnd: 0));
        await RegisterAsync(mobile, Endpoint2);

        await IngestAsync(desktop, SecurityAlert("security-1", DateTimeOffset.UtcNow));
        await DispatchAsync(factory);

        await using (var db = CreateContext(connectionString))
        {
            var notification = await db.Notifications.AsNoTracking().SingleAsync();
            Assert.Equal(NotificationStatus.Failed, notification.Status);
            Assert.Equal("endpoint_invalid", notification.FailureCode);
            Assert.Equal(1, notification.AttemptCount);

            var registration = await db.PushRegistrations.AsNoTracking().SingleAsync();
            Assert.NotNull(registration.InvalidatedAtUtc);
        }

        await DispatchAsync(factory);
        Assert.Equal(1, fake.Calls);
    }

    [Fact]
    public async Task Transient_failures_retry_until_max_attempts()
    {
        var connectionString = TestConnectionString();
        if (connectionString is null)
        {
            return;
        }

        await ResetDatabaseAsync(connectionString);
        var fake = new FakePushSender();
        fake.FailTransient();
        fake.FailTransient();
        fake.FailTransient();
        await using var factory = CreateFactory(connectionString, fake);
        using var anonymous = factory.CreateClient();
        var paired = await PairAsync(anonymous);
        using var desktop = CreateClient(factory, paired.DesktopToken);
        using var mobile = CreateClient(factory, paired.MobileToken);
        await PutPreferencesAsync(mobile, Preferences(quietStart: 0, quietEnd: 0));
        await RegisterAsync(mobile);

        await IngestAsync(desktop, SecurityAlert("security-1", DateTimeOffset.UtcNow));

        await DispatchAsync(factory);
        await DispatchAsync(factory);
        Assert.Equal(1, fake.Calls);
        await using (var db = CreateContext(connectionString))
        {
            var notification = await db.Notifications.AsNoTracking().SingleAsync();
            Assert.Equal(NotificationStatus.Pending, notification.Status);
            Assert.Equal(1, notification.AttemptCount);
            Assert.Equal("unavailable", notification.FailureCode);
        }

        await ShiftLastAttemptAsync(connectionString);
        await DispatchAsync(factory);
        Assert.Equal(2, fake.Calls);

        await ShiftLastAttemptAsync(connectionString);
        await DispatchAsync(factory);
        Assert.Equal(3, fake.Calls);

        await using (var db = CreateContext(connectionString))
        {
            var notification = await db.Notifications.AsNoTracking().SingleAsync();
            Assert.Equal(NotificationStatus.Failed, notification.Status);
            Assert.Equal(3, notification.AttemptCount);
        }

        await DispatchAsync(factory);
        Assert.Equal(3, fake.Calls);
    }

    [Fact]
    public async Task Registration_and_preferences_endpoints_validate_input()
    {
        var connectionString = TestConnectionString();
        if (connectionString is null)
        {
            return;
        }

        await ResetDatabaseAsync(connectionString);
        var fake = new FakePushSender();
        await using var factory = CreateFactory(connectionString, fake);
        using var anonymous = factory.CreateClient();
        var paired = await PairAsync(anonymous);
        using var desktop = CreateClient(factory, paired.DesktopToken);
        using var mobile = CreateClient(factory, paired.MobileToken);

        var desktopRegistration = await desktop.PostAsJsonAsync(
            "/v1/push/registrations",
            new RegisterPushRequest(Endpoint1, P256dh, Auth, "windows"));
        Assert.Equal(HttpStatusCode.Forbidden, desktopRegistration.StatusCode);

        await RegisterAsync(mobile, Endpoint1);
        await RegisterAsync(mobile, Endpoint2);

        await using (var db = CreateContext(connectionString))
        {
            var registration = await db.PushRegistrations.AsNoTracking().SingleAsync();
            Assert.Equal(Endpoint2, registration.Endpoint);
            Assert.Equal("android", registration.Platform);
        }

        var defaults = await mobile.GetFromJsonAsync<NotificationPreferencesDto>(
            "/v1/preferences", Json);
        Assert.NotNull(defaults);
        Assert.True(defaults.AttentionAlert);
        Assert.True(defaults.DailySummary);
        Assert.True(defaults.ProtectionOff);
        Assert.True(defaults.SecurityAlert);
        Assert.True(defaults.BlockedAttempt);
        Assert.True(defaults.SecurityAlertBypassQuietHours);
        Assert.Equal(1320, defaults.QuietStartMinutes);
        Assert.Equal(360, defaults.QuietEndMinutes);
        Assert.Equal(1230, defaults.DailySummaryMinutes);
        Assert.Equal(TimeZone, defaults.TimeZoneId);

        var invalidMinutes = await mobile.PutAsJsonAsync(
            "/v1/preferences",
            Preferences(1440, 360));
        Assert.Equal(HttpStatusCode.BadRequest, invalidMinutes.StatusCode);
        var invalidMinutesBody = await invalidMinutes.Content.ReadFromJsonAsync<ApiErrorResponse>(Json);
        Assert.Equal("invalid_preferences", invalidMinutesBody!.Error.Code);

        var invalidTimeZone = await mobile.PutAsJsonAsync(
            "/v1/preferences",
            Preferences(0, 0, timeZoneId: "Mars/Olympus"));
        Assert.Equal(HttpStatusCode.BadRequest, invalidTimeZone.StatusCode);
        var invalidTimeZoneBody = await invalidTimeZone.Content.ReadFromJsonAsync<ApiErrorResponse>(Json);
        Assert.Equal("invalid_time_zone", invalidTimeZoneBody!.Error.Code);

        var update = await mobile.PutAsJsonAsync(
            "/v1/preferences",
            new UpdateNotificationPreferencesRequest(
                false, true, false, true, 0, 0, 1200, "Asia/Tokyo", false, false));
        Assert.Equal(HttpStatusCode.OK, update.StatusCode);
        var updated = await update.Content.ReadFromJsonAsync<NotificationPreferencesDto>(Json);
        Assert.NotNull(updated);
        Assert.False(updated.AttentionAlert);
        Assert.False(updated.BlockedAttempt);
        Assert.Equal("Asia/Tokyo", updated.TimeZoneId);
        Assert.Equal(1200, updated.DailySummaryMinutes);

        var reloaded = await mobile.GetFromJsonAsync<NotificationPreferencesDto>(
            "/v1/preferences", Json);
        Assert.Equal(updated, reloaded);

        await using (var db = CreateContext(connectionString))
        {
            var family = await db.Families.AsNoTracking().SingleAsync();
            Assert.Equal("Asia/Tokyo", family.TimeZoneId);

            var audit = await db.CloudAuditLogs.AsNoTracking()
                .SingleAsync(a => a.Action == "NotificationPreferencesUpdated");
            Assert.DoesNotContain("efm_", audit.MetadataJson);
            Assert.DoesNotContain("efd_", audit.MetadataJson);
        }
    }

    private static string? TestConnectionString() =>
        Environment.GetEnvironmentVariable(TestDbVariable) is { Length: > 0 } value
            ? value
            : null;

    private static CloudApiFactory CreateFactory(
        string connectionString,
        FakePushSender fake,
        IReadOnlyDictionary<string, string>? settings = null,
        Action<IServiceCollection>? configureServices = null) =>
        new(connectionString, settings, services =>
        {
            services.RemoveAll<IPushSender>();
            services.AddSingleton<IPushSender>(fake);
            configureServices?.Invoke(services);
        });

    private static HttpClient CreateClient(CloudApiFactory factory, string token)
    {
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return client;
    }

    private static async Task<PairedFamily> PairAsync(HttpClient anonymous)
    {
        var createResponse = await anonymous.PostAsJsonAsync(
            "/v1/pairing/sessions",
            new CreatePairingSessionRequest("Student PC", "windows"));
        Assert.Equal(HttpStatusCode.OK, createResponse.StatusCode);
        var session = await createResponse.Content.ReadFromJsonAsync<PairingSessionResponse>(Json);
        Assert.NotNull(session);

        var claimResponse = await anonymous.PostAsJsonAsync(
            "/v1/pairing/claim",
            new ClaimPairingRequest(
                session.Code,
                "Parent phone",
                "android",
                "1.0.0",
                session.SessionId.ToString(),
                TimeZone));
        Assert.Equal(HttpStatusCode.OK, claimResponse.StatusCode);
        var claimed = await claimResponse.Content.ReadFromJsonAsync<ClaimPairingResponse>(Json);
        Assert.NotNull(claimed);

        return new PairedFamily(session.DesktopToken, claimed.MobileToken);
    }

    private static async Task RegisterAsync(HttpClient mobile, string? endpoint = null)
    {
        var response = await mobile.PostAsJsonAsync(
            "/v1/push/registrations",
            RegisterRequest(endpoint));
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    private static RegisterPushRequest RegisterRequest(string? endpoint = null) =>
        new(endpoint ?? Endpoint1, P256dh, Auth, "android", "1.0.0");

    private static async Task PutPreferencesAsync(
        HttpClient mobile,
        UpdateNotificationPreferencesRequest preferences)
    {
        var response = await mobile.PutAsJsonAsync("/v1/preferences", preferences);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    private static UpdateNotificationPreferencesRequest Preferences(
        int quietStart,
        int quietEnd,
        bool attentionAlert = true,
        string timeZoneId = TimeZone) =>
        new(true, true, true, true, quietStart, quietEnd, 1230, timeZoneId)
        {
            AttentionAlert = attentionAlert,
        };

    private static async Task<IReadOnlyList<SyncEventResult>> IngestAsync(
        HttpClient desktop,
        params SyncEventDto[] events)
    {
        var response = await desktop.PostAsJsonAsync(
            "/v1/sync/events",
            new SyncBatchRequest(events));
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<SyncBatchResponse>(Json);
        Assert.NotNull(body);
        return body.Results;
    }

    private static SyncEventDto AttentionAlert(string clientEventId, DateTimeOffset occurredAtUtc)
    {
        var localDate = LocalToday().ToString("yyyy-MM-dd");
        return new SyncEventDto(
            clientEventId,
            SyncEventType.AttentionAlert,
            occurredAtUtc,
            JsonSerializer.SerializeToElement(new
            {
                kind = "distracted",
                localDate,
                startedAtUtc = occurredAtUtc.AddMinutes(-6),
                durationSeconds = 360,
            }));
    }

    private static SyncEventDto SecurityAlert(string clientEventId, DateTimeOffset occurredAtUtc)
    {
        var localDate = LocalToday().ToString("yyyy-MM-dd");
        return new SyncEventDto(
            clientEventId,
            SyncEventType.SecurityAlert,
            occurredAtUtc,
            JsonSerializer.SerializeToElement(new
            {
                kind = "clock_jump",
                localDate,
            }));
    }

    private static async Task DispatchAsync(CloudApiFactory factory)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var dispatcher = scope.ServiceProvider.GetRequiredService<PushDispatchService>();
        await dispatcher.DispatchPendingAsync(CancellationToken.None);
    }

    private static async Task ShiftLastAttemptAsync(string connectionString)
    {
        await using var db = CreateContext(connectionString);
        await db.Notifications.ExecuteUpdateAsync(
            setters => setters.SetProperty(
                n => n.LastAttemptAtUtc,
                DateTimeOffset.UtcNow.AddMinutes(-10)));
    }

    private static async Task WaitUntilAsync(Func<bool> condition, TimeSpan timeout)
    {
        var deadline = DateTimeOffset.UtcNow + timeout;
        while (DateTimeOffset.UtcNow < deadline)
        {
            if (condition())
            {
                return;
            }

            await Task.Delay(25);
        }

        Assert.True(condition());
    }

    private static async Task<JsonElement> ReceiveJsonAsync(WebSocket socket, TimeSpan timeout)
    {
        using var cancellation = new CancellationTokenSource(timeout);
        var buffer = new byte[4096];
        while (true)
        {
            using var message = new MemoryStream();
            WebSocketReceiveResult result;
            do
            {
                result = await socket.ReceiveAsync(
                    new ArraySegment<byte>(buffer),
                    cancellation.Token);
                if (result.MessageType == WebSocketMessageType.Close)
                {
                    Assert.Fail("WebSocket closed before a push message arrived.");
                }

                message.Write(buffer, 0, result.Count);
            }
            while (!result.EndOfMessage);

            using var document = JsonDocument.Parse(message.ToArray());
            var root = document.RootElement.Clone();
            if (root.TryGetProperty("type", out var type)
                && string.Equals(type.GetString(), "heartbeat", StringComparison.Ordinal))
            {
                continue;
            }

            return root;
        }
    }

    private static DateTimeOffset FloorToQuarterHour(DateTimeOffset value)
    {
        var window = TimeSpan.FromMinutes(15);
        return new DateTimeOffset(value.UtcTicks - (value.UtcTicks % window.Ticks), TimeSpan.Zero);
    }

    private static DateOnly LocalToday()
    {
        var timeZone = TimeZoneInfo.FindSystemTimeZoneById(TimeZone);
        return DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(DateTimeOffset.UtcNow, timeZone).DateTime);
    }

    private static async Task ResetDatabaseAsync(string connectionString)
    {
        await using var db = CreateContext(connectionString);
        await db.Database.EnsureDeletedAsync();
        await db.Database.MigrateAsync();
    }

    private static CloudDbContext CreateContext(string connectionString) =>
            new(new DbContextOptionsBuilder<CloudDbContext>().UseSqlite(connectionString).Options);

    private sealed record PairedFamily(string DesktopToken, string MobileToken);
}

internal sealed class FakePushSender : IPushSender
{
    private readonly Queue<Func<IReadOnlyList<PushTarget>, IReadOnlyList<PushSendResult>>> _behaviors = new();

    public List<PushTarget> Messages { get; } = [];

    public IReadOnlyDictionary<string, string>? Data { get; private set; }

    public int Calls { get; private set; }

    public void FailTransient(string code = "Unavailable") =>
        _behaviors.Enqueue(targets => targets
            .Select(t => new PushSendResult(t.Endpoint, false, false, code))
            .ToList());

    public void FailInvalid() =>
        _behaviors.Enqueue(targets => targets
            .Select(t => new PushSendResult(t.Endpoint, false, true, "Gone"))
            .ToList());

    public Task<IReadOnlyList<PushSendResult>> SendAsync(
        IReadOnlyList<PushTarget> targets,
        IReadOnlyDictionary<string, string> data,
        CancellationToken cancellationToken)
    {
        Calls++;
        Messages.AddRange(targets);
        Data = data;

        var behavior = _behaviors.Count > 0
            ? _behaviors.Dequeue()
            : (Func<IReadOnlyList<PushTarget>, IReadOnlyList<PushSendResult>>)Succeed;
        return Task.FromResult(behavior(targets));
    }

    private static IReadOnlyList<PushSendResult> Succeed(IReadOnlyList<PushTarget> targets) =>
        targets.Select(t => new PushSendResult(t.Endpoint, true, false, null)).ToList();
}
