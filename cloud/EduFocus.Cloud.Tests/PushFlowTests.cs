using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
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
using SyncEventType = EduFocus.Cloud.Contracts.Sync.SyncEventType;

namespace EduFocus.Cloud.Tests;

[Collection("Postgres")]
public sealed class PushFlowTests
{
    private const string TestDbVariable = "EDUFOCUS_CLOUD_TEST_DB";
    private const string TimeZone = "Asia/Ho_Chi_Minh";

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) },
    };

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
            new RegisterPushRequest("fcm-token-1", "android", "1.0.0"));
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
        Assert.Equal("fcm-token-1", registration.FcmToken);

        var message = Assert.Single(fake.Messages);
        Assert.Equal("fcm-token-1", message.Token);
        Assert.Equal(3, message.Data.Count);
        Assert.Equal("attention_alert", message.Data["type"]);
        Assert.Equal(LocalToday().ToString("yyyy-MM-dd"), message.Data["date"]);
        Assert.Equal(notification.Id.ToString(), message.Data["notificationId"]);
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
        await RegisterAsync(mobile, "fcm-token-1");

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
        await RegisterAsync(mobile, "fcm-token-1");

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

        var message = Assert.Single(fake.Messages);
        Assert.Equal("security_alert", message.Data["type"]);
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
    public async Task Invalid_token_is_invalidated_and_not_retried()
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
        await RegisterAsync(mobile, "fcm-bad-token");

        await IngestAsync(desktop, SecurityAlert("security-1", DateTimeOffset.UtcNow));
        await DispatchAsync(factory);

        await using (var db = CreateContext(connectionString))
        {
            var notification = await db.Notifications.AsNoTracking().SingleAsync();
            Assert.Equal(NotificationStatus.Failed, notification.Status);
            Assert.Equal("token_invalid", notification.FailureCode);
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
        await RegisterAsync(mobile, "fcm-token-1");

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
            new RegisterPushRequest("fcm-token-1", "windows"));
        Assert.Equal(HttpStatusCode.Forbidden, desktopRegistration.StatusCode);

        await RegisterAsync(mobile, "fcm-token-1");
        await RegisterAsync(mobile, "fcm-token-2");

        await using (var db = CreateContext(connectionString))
        {
            var registration = await db.PushRegistrations.AsNoTracking().SingleAsync();
            Assert.Equal("fcm-token-2", registration.FcmToken);
            Assert.Equal("android", registration.Platform);
        }

        var defaults = await mobile.GetFromJsonAsync<NotificationPreferencesDto>(
            "/v1/preferences", Json);
        Assert.NotNull(defaults);
        Assert.True(defaults.AttentionAlert);
        Assert.True(defaults.DailySummary);
        Assert.True(defaults.ProtectionOff);
        Assert.True(defaults.SecurityAlert);
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
                false, true, false, true, 0, 0, 1200, "Asia/Tokyo", false));
        Assert.Equal(HttpStatusCode.OK, update.StatusCode);
        var updated = await update.Content.ReadFromJsonAsync<NotificationPreferencesDto>(Json);
        Assert.NotNull(updated);
        Assert.False(updated.AttentionAlert);
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

    private static CloudApiFactory CreateFactory(string connectionString, FakePushSender fake) =>
        new(connectionString, configureServices: services =>
        {
            services.RemoveAll<IFcmPushSender>();
            services.AddSingleton<IFcmPushSender>(fake);
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

    private static async Task RegisterAsync(HttpClient mobile, string token)
    {
        var response = await mobile.PostAsJsonAsync(
            "/v1/push/registrations",
            new RegisterPushRequest(token, "android", "1.0.0"));
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

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
        new(new DbContextOptionsBuilder<CloudDbContext>().UseNpgsql(connectionString).Options);

    private sealed record PairedFamily(string DesktopToken, string MobileToken);
}

internal sealed class FakePushSender : IFcmPushSender
{
    private readonly Queue<Func<IReadOnlyList<PushMessage>, IReadOnlyList<PushSendResult>>> _behaviors = new();

    public List<PushMessage> Messages { get; } = [];

    public int Calls { get; private set; }

    public void FailTransient(string code = "Unavailable") =>
        _behaviors.Enqueue(messages => messages
            .Select(m => new PushSendResult(m.Token, false, false, code))
            .ToList());

    public void FailInvalid() =>
        _behaviors.Enqueue(messages => messages
            .Select(m => new PushSendResult(m.Token, false, true, "Unregistered"))
            .ToList());

    public Task<IReadOnlyList<PushSendResult>> SendAsync(
        IReadOnlyList<PushMessage> messages,
        CancellationToken cancellationToken)
    {
        Calls++;
        Messages.AddRange(messages);

        var behavior = _behaviors.Count > 0
            ? _behaviors.Dequeue()
            : (Func<IReadOnlyList<PushMessage>, IReadOnlyList<PushSendResult>>)Succeed;
        return Task.FromResult(behavior(messages));
    }

    private static IReadOnlyList<PushSendResult> Succeed(IReadOnlyList<PushMessage> messages) =>
        messages.Select(m => new PushSendResult(m.Token, true, false, null)).ToList();
}
