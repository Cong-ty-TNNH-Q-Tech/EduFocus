using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using EduFocus.Cloud.Contracts.Common;
using EduFocus.Cloud.Contracts.Devices;
using EduFocus.Cloud.Contracts.Notifications;
using EduFocus.Cloud.Contracts.Pairing;
using EduFocus.Cloud.Contracts.Push;
using EduFocus.Cloud.Contracts.Reports;
using EduFocus.Cloud.Contracts.Sync;
using EduFocus.Cloud.Data;
using EduFocus.Cloud.Data.Entities;
using Microsoft.EntityFrameworkCore;
using SyncEventType = EduFocus.Cloud.Contracts.Sync.SyncEventType;

namespace EduFocus.Cloud.Tests;

[Collection("CloudDatabase")]
public sealed class ReadApiTests
{
    private const string TestDbVariable = "EDUFOCUS_CLOUD_TEST_DB";
    private const string TimeZone = "Asia/Ho_Chi_Minh";
    private const string PushEndpoint = "https://push.example.com/sub/device-1";
    private const string PushP256dh = "BAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA";
    private const string PushAuth = "AAAAAAAAAAAAAAAAAAAAAA";

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) },
    };

    [Fact]
    public async Task Family_data_is_isolated_by_token()
    {
        var connectionString = TestConnectionString();
        if (connectionString is null)
        {
            return;
        }

        await ResetDatabaseAsync(connectionString);
        await using var factory = new CloudApiFactory(connectionString);
        using var anonymous = factory.CreateClient();
        var familyA = await PairAsync(anonymous);
        var familyB = await PairAsync(anonymous);
        using var desktopA = CreateClient(factory, familyA.DesktopToken);
        using var desktopB = CreateClient(factory, familyB.DesktopToken);
        using var mobileA = CreateClient(factory, familyA.MobileToken);

        var today = LocalToday();
        await IngestAsync(desktopA, Summary("summary-a", today, studySeconds: 3600));
        await IngestAsync(desktopB, Summary("summary-b", today, studySeconds: 7200));
        await IngestAsync(desktopA, SecurityAlert("security-a", DateTimeOffset.UtcNow));
        await IngestAsync(desktopB, SecurityAlert("security-b", DateTimeOffset.UtcNow));

        var report = await mobileA.GetFromJsonAsync<List<DailySummaryDto>>(
            $"/v1/reports/daily?from={today:yyyy-MM-dd}&to={today:yyyy-MM-dd}", Json);
        var day = Assert.Single(report!);
        Assert.Equal(3600, day.StudySeconds);

        var notifications = await mobileA.GetFromJsonAsync<NotificationListResponse>(
            "/v1/notifications", Json);
        var notification = Assert.Single(notifications!.Items);
        Assert.Equal(NotificationKind.SecurityAlert, notification.Category);

        var crossFamilyRevoke = await mobileA.DeleteAsync($"/v1/devices/{familyB.MobileDeviceId}");
        Assert.Equal(HttpStatusCode.NotFound, crossFamilyRevoke.StatusCode);
    }

    [Fact]
    public async Task Report_range_is_validated()
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

        var reversed = await mobile.GetAsync("/v1/reports/daily?from=2026-11-10&to=2026-11-01");
        Assert.Equal(HttpStatusCode.BadRequest, reversed.StatusCode);
        Assert.Equal("invalid_range", await ReadErrorCodeAsync(reversed));

        var tooLong = await mobile.GetAsync("/v1/reports/daily?from=2026-01-01&to=2026-03-01");
        Assert.Equal(HttpStatusCode.BadRequest, tooLong.StatusCode);
        Assert.Equal("invalid_range", await ReadErrorCodeAsync(tooLong));

        var missing = await mobile.GetAsync("/v1/reports/daily?from=2026-11-01");
        Assert.Equal(HttpStatusCode.BadRequest, missing.StatusCode);

        var valid = await mobile.GetAsync("/v1/reports/daily?from=2026-11-01&to=2026-12-01");
        Assert.Equal(HttpStatusCode.OK, valid.StatusCode);
        Assert.Empty((await valid.Content.ReadFromJsonAsync<List<DailySummaryDto>>(Json))!);
    }

    [Fact]
    public async Task Report_returns_latest_summary_per_day()
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
        using var desktop = CreateClient(factory, paired.DesktopToken);
        using var mobile = CreateClient(factory, paired.MobileToken);

        var today = LocalToday();
        var generatedAt = DateTimeOffset.UtcNow;
        await IngestAsync(desktop, Summary("summary-1", today, 3600, generatedAt));
        var newer = generatedAt.AddMinutes(5);
        await IngestAsync(desktop, Summary("summary-2", today, 7200, newer));

        var report = await mobile.GetFromJsonAsync<List<DailySummaryDto>>(
            $"/v1/reports/daily?from={today:yyyy-MM-dd}&to={today:yyyy-MM-dd}", Json);
        var day = Assert.Single(report!);
        Assert.Equal(7200, day.StudySeconds);
        Assert.Equal(TimeZone, day.TimeZoneId);
    }

    [Fact]
    public async Task Notifications_pagination_is_stable_with_new_records()
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

        var baseTime = DateTimeOffset.UtcNow.AddMinutes(-30);
        await using (var db = CreateContext(connectionString))
        {
            for (var index = 1; index <= 5; index++)
            {
                db.Notifications.Add(SeedNotification(
                    paired.FamilyId,
                    $"paging:{index}",
                    baseTime.AddMinutes(-index),
                    NotificationStatus.Sent));
            }

            await db.SaveChangesAsync();
        }

        var firstPage = await mobile.GetFromJsonAsync<NotificationListResponse>(
            "/v1/notifications?limit=2", Json);
        Assert.Equal(2, firstPage!.Items.Count);
        Assert.NotNull(firstPage.NextBefore);

        await using (var db = CreateContext(connectionString))
        {
            db.Notifications.Add(SeedNotification(
                paired.FamilyId,
                "paging:new",
                baseTime,
                NotificationStatus.Pending));
            await db.SaveChangesAsync();
        }

        var secondPage = await mobile.GetFromJsonAsync<NotificationListResponse>(
            $"/v1/notifications?limit=2&before={Uri.EscapeDataString(firstPage.NextBefore)}", Json);
        Assert.Equal(2, secondPage!.Items.Count);
        Assert.NotNull(secondPage.NextBefore);

        var thirdPage = await mobile.GetFromJsonAsync<NotificationListResponse>(
            $"/v1/notifications?limit=2&before={Uri.EscapeDataString(secondPage.NextBefore)}", Json);
        var last = Assert.Single(thirdPage!.Items);
        Assert.Null(thirdPage.NextBefore);

        var allIds = firstPage.Items
            .Concat(secondPage.Items)
            .Append(last)
            .Select(item => item.Id)
            .ToList();
        Assert.Equal(5, allIds.Count);
        Assert.Equal(5, allIds.Distinct().Count());

        var invalidCursor = await mobile.GetAsync("/v1/notifications?before=not-a-cursor");
        Assert.Equal(HttpStatusCode.BadRequest, invalidCursor.StatusCode);
        Assert.Equal("invalid_cursor", await ReadErrorCodeAsync(invalidCursor));

        var invalidLimit = await mobile.GetAsync("/v1/notifications?limit=101");
        Assert.Equal(HttpStatusCode.BadRequest, invalidLimit.StatusCode);
        Assert.Equal("invalid_limit", await ReadErrorCodeAsync(invalidLimit));
    }

    [Fact]
    public async Task Devices_can_be_listed_and_revoked_within_family()
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
        using var desktop = CreateClient(factory, paired.DesktopToken);
        using var mobile = CreateClient(factory, paired.MobileToken);

        var devices = await mobile.GetFromJsonAsync<DeviceListResponse>("/v1/devices", Json);
        Assert.Equal(2, devices!.Devices.Count);
        var desktopDevice = Assert.Single(devices.Devices, device => device.Kind == "Desktop");
        var mobileDevice = Assert.Single(devices.Devices, device => device.Kind == "Mobile");
        Assert.Null(desktopDevice.RevokedAtUtc);

        var revokeDesktop = await mobile.DeleteAsync($"/v1/devices/{desktopDevice.Id}");
        Assert.Equal(HttpStatusCode.OK, revokeDesktop.StatusCode);

        var desktopAfterRevoke = await desktop.GetAsync("/v1/link/status");
        Assert.Equal(HttpStatusCode.Unauthorized, desktopAfterRevoke.StatusCode);

        var afterRevoke = await mobile.GetFromJsonAsync<DeviceListResponse>("/v1/devices", Json);
        Assert.NotNull(afterRevoke!.Devices.Single(device => device.Id == desktopDevice.Id).RevokedAtUtc);

        var selfRevoke = await mobile.DeleteAsync($"/v1/devices/{mobileDevice.Id}");
        Assert.Equal(HttpStatusCode.OK, selfRevoke.StatusCode);

        using var oldMobile = CreateClient(factory, paired.MobileToken);
        var mobileAfterRevoke = await oldMobile.GetAsync("/v1/link/status");
        Assert.Equal(HttpStatusCode.Unauthorized, mobileAfterRevoke.StatusCode);
    }

    [Fact]
    public async Task Deleting_family_requires_confirmation_and_wipes_data()
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
        using var desktop = CreateClient(factory, paired.DesktopToken);
        using var mobile = CreateClient(factory, paired.MobileToken);

        var today = LocalToday();
        await IngestAsync(
            desktop,
            Summary("summary-1", today, 3600),
            SecurityAlert("security-1", DateTimeOffset.UtcNow));
        var register = await mobile.PostAsJsonAsync(
            "/v1/push/registrations",
            new RegisterPushRequest(PushEndpoint, PushP256dh, PushAuth, "android"));
        Assert.Equal(HttpStatusCode.OK, register.StatusCode);

        using var wrongRequest = new HttpRequestMessage(HttpMethod.Delete, "/v1/family")
        {
            Content = JsonContent.Create(new { confirmation = "xoa" }, options: Json),
        };
        var wrongConfirmation = await mobile.SendAsync(wrongRequest);
        Assert.Equal(HttpStatusCode.BadRequest, wrongConfirmation.StatusCode);
        Assert.Equal("confirmation_required", await ReadErrorCodeAsync(wrongConfirmation));

        using var deleteRequest = new HttpRequestMessage(HttpMethod.Delete, "/v1/family")
        {
            Content = JsonContent.Create(new { confirmation = "XÓA" }, options: Json),
        };
        var deleteResponse = await mobile.SendAsync(deleteRequest);
        Assert.Equal(HttpStatusCode.OK, deleteResponse.StatusCode);

        await using (var db = CreateContext(connectionString))
        {
            Assert.Equal(0, await db.Families.CountAsync());
            Assert.Equal(0, await db.Devices.CountAsync());
            Assert.Equal(0, await db.DeviceTokens.CountAsync());
            Assert.Equal(0, await db.PairingSessions.CountAsync());
            Assert.Equal(0, await db.PushRegistrations.CountAsync());
            Assert.Equal(0, await db.NotificationPreferences.CountAsync());
            Assert.Equal(0, await db.DailySummaries.CountAsync());
            Assert.Equal(0, await db.SyncEvents.CountAsync());
            Assert.Equal(0, await db.Notifications.CountAsync());

            var deletionAudit = await db.CloudAuditLogs.AsNoTracking()
                .SingleAsync(a => a.Action == "FamilyDeleted");
            Assert.Null(deletionAudit.FamilyId);
            Assert.Null(deletionAudit.ActorDeviceId);
        }

        using var oldMobile = CreateClient(factory, paired.MobileToken);
        var afterDelete = await oldMobile.GetAsync("/v1/link/status");
        Assert.Equal(HttpStatusCode.Unauthorized, afterDelete.StatusCode);
    }

    private static NotificationRecord SeedNotification(
        Guid familyId,
        string dedupeKey,
        DateTimeOffset createdAtUtc,
        NotificationStatus status) =>
        new()
        {
            Id = Guid.NewGuid(),
            FamilyId = familyId,
            Category = NotificationCategory.SecurityAlert,
            DedupeKey = dedupeKey,
            CreatedAtUtc = createdAtUtc,
            Status = status,
            SentAtUtc = status == NotificationStatus.Sent ? createdAtUtc : null,
        };

    private static string? TestConnectionString() =>
        Environment.GetEnvironmentVariable(TestDbVariable) is { Length: > 0 } value
            ? value
            : null;

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

        return new PairedFamily(
            session.DesktopToken,
            claimed.MobileToken,
            claimed.FamilyId,
            claimed.DeviceId);
    }

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

    private static SyncEventDto Summary(
        string clientEventId,
        DateOnly localDate,
        long studySeconds,
        DateTimeOffset? generatedAtUtc = null)
    {
        var generatedAt = generatedAtUtc ?? DateTimeOffset.UtcNow;
        return new SyncEventDto(
            clientEventId,
            SyncEventType.DailySummary,
            generatedAt,
            JsonSerializer.SerializeToElement(new
            {
                localDate = localDate.ToString("yyyy-MM-dd"),
                timeZoneId = TimeZone,
                generatedAtUtc = generatedAt,
                sessionsCount = 1,
                activeSeconds = studySeconds,
                studySeconds,
                idleSeconds = 0,
                onlineClassSeconds = 0,
                monitoringEligibleSeconds = studySeconds,
                monitoredSeconds = studySeconds,
                focusedSeconds = studySeconds,
                distractedSeconds = 0,
                absentSeconds = 0,
                drowsySeconds = 0,
                blockedAttempts = 0,
                attentionPercent = 90.0,
                coveragePercent = 95.0,
            }));
    }

    private static SyncEventDto SecurityAlert(string clientEventId, DateTimeOffset occurredAtUtc) =>
        new(
            clientEventId,
            SyncEventType.SecurityAlert,
            occurredAtUtc,
            JsonSerializer.SerializeToElement(new
            {
                kind = "clock_jump",
                localDate = LocalToday().ToString("yyyy-MM-dd"),
            }));

    private static async Task<string?> ReadErrorCodeAsync(HttpResponseMessage response)
    {
        var body = await response.Content.ReadFromJsonAsync<ApiErrorResponse>(Json);
        return body?.Error.Code;
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

    private sealed record PairedFamily(
        string DesktopToken,
        string MobileToken,
        Guid FamilyId,
        Guid MobileDeviceId);
}
