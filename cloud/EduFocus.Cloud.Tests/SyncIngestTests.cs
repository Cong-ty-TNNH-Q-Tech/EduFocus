using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using EduFocus.Cloud.Contracts.Json;
using EduFocus.Cloud.Contracts.Pairing;
using EduFocus.Cloud.Contracts.Sync;
using EduFocus.Cloud.Data;
using EduFocus.Cloud.Data.Entities;
using Microsoft.EntityFrameworkCore;
using EntitySyncEventStatus = EduFocus.Cloud.Data.Entities.SyncEventStatus;
using SyncEventType = EduFocus.Cloud.Contracts.Sync.SyncEventType;

namespace EduFocus.Cloud.Tests;

[Collection("Postgres")]
public sealed class SyncIngestTests
{
    private const string TestDbVariable = "EDUFOCUS_CLOUD_TEST_DB";

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) },
    };

    [Fact]
    public async Task Ingest_is_idempotent_and_upserts_daily_summary()
    {
        var connectionString = Environment.GetEnvironmentVariable(TestDbVariable);
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            return;
        }

        await ResetDatabaseAsync(connectionString);
        await using var factory = new CloudApiFactory(connectionString);
        using var anonymous = factory.CreateClient();
        var desktopToken = await CreateDesktopTokenAsync(anonymous);
        using var desktop = CreateAuthenticatedClient(factory, desktopToken);

        var today = LocalToday();
        var generatedAtUtc = DateTimeOffset.UtcNow;
        var summaryEvent = new SyncEventDto(
            "summary-1",
            SyncEventType.DailySummary,
            generatedAtUtc,
            SummaryPayload(today, generatedAtUtc, studySeconds: 3600));
        var alertEvent = new SyncEventDto(
            "alert-1",
            SyncEventType.AttentionAlert,
            generatedAtUtc,
            JsonSerializer.SerializeToElement(new
            {
                kind = "distracted",
                localDate = today.ToString("yyyy-MM-dd"),
                startedAtUtc = generatedAtUtc.AddMinutes(-6),
                durationSeconds = 360,
            }));

        var first = await PostBatchAsync(desktop, summaryEvent, alertEvent);
        Assert.Equal(SyncEventResultStatus.Applied, first[0].Status);
        Assert.Equal(SyncEventResultStatus.Applied, first[1].Status);

        await using (var db = CreateContext(connectionString))
        {
            var summary = await db.DailySummaries.AsNoTracking().SingleAsync();
            Assert.Equal(3600, summary.StudySeconds);
            Assert.Equal(85.7, summary.AttentionPercent);

            var events = await db.SyncEvents.AsNoTracking().ToListAsync();
            Assert.Equal(2, events.Count);
            Assert.Equal(
                EntitySyncEventStatus.Processed,
                events.Single(e => e.ClientEventId == "summary-1").Status);
            Assert.Equal(
                EntitySyncEventStatus.Pending,
                events.Single(e => e.ClientEventId == "alert-1").Status);
        }

        var repeat = await PostBatchAsync(desktop, summaryEvent, alertEvent);
        Assert.All(repeat, result => Assert.Equal(SyncEventResultStatus.Duplicate, result.Status));

        var newerGeneratedAt = generatedAtUtc.AddMinutes(5);
        var newer = await PostBatchAsync(desktop, new SyncEventDto(
            "summary-2",
            SyncEventType.DailySummary,
            newerGeneratedAt,
            SummaryPayload(today, newerGeneratedAt, studySeconds: 7200)));
        Assert.Equal(SyncEventResultStatus.Applied, newer[0].Status);

        var olderGeneratedAt = generatedAtUtc.AddMinutes(1);
        var older = await PostBatchAsync(desktop, new SyncEventDto(
            "summary-3",
            SyncEventType.DailySummary,
            olderGeneratedAt,
            SummaryPayload(today, olderGeneratedAt, studySeconds: 1800)));
        Assert.Equal(SyncEventResultStatus.Applied, older[0].Status);

        var stale = await PostBatchAsync(desktop, new SyncEventDto(
            "summary-stale",
            SyncEventType.DailySummary,
            generatedAtUtc,
            SummaryPayload(today.AddDays(-8), generatedAtUtc, studySeconds: 900)));
        Assert.Equal(SyncEventResultStatus.Rejected, stale[0].Status);
        Assert.Equal("stale_summary", stale[0].Code);

        await using (var db = CreateContext(connectionString))
        {
            var summary = await db.DailySummaries.AsNoTracking().SingleAsync();
            Assert.Equal(7200, summary.StudySeconds);
            Assert.Equal(5, await db.SyncEvents.CountAsync());
        }
    }

    [Fact]
    public async Task Batch_limits_and_payload_whitelist_are_enforced()
    {
        var connectionString = Environment.GetEnvironmentVariable(TestDbVariable);
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            return;
        }

        await ResetDatabaseAsync(connectionString);
        await using var factory = new CloudApiFactory(connectionString);
        using var anonymous = factory.CreateClient();
        var desktopToken = await CreateDesktopTokenAsync(anonymous);
        using var desktop = CreateAuthenticatedClient(factory, desktopToken);

        var today = LocalToday().ToString("yyyy-MM-dd");
        var now = DateTimeOffset.UtcNow;

        var tooMany = new SyncBatchRequest(Enumerable.Range(0, 51)
            .Select(i => new SyncEventDto(
                $"alert-{i}",
                SyncEventType.SecurityAlert,
                now,
                JsonSerializer.SerializeToElement(new { kind = "clock_jump", localDate = today })))
            .ToList());
        var tooManyResponse = await desktop.PostAsJsonAsync("/v1/sync/events", tooMany);
        Assert.Equal(HttpStatusCode.BadRequest, tooManyResponse.StatusCode);
        var tooManyBody = await tooManyResponse.Content.ReadFromJsonAsync<EduFocus.Cloud.Contracts.Common.ApiErrorResponse>(Json);
        Assert.Equal("too_many_events", tooManyBody!.Error.Code);

        var exactlyFifty = new SyncBatchRequest(Enumerable.Range(0, 50)
            .Select(i => new SyncEventDto(
                $"alert-{i}",
                SyncEventType.SecurityAlert,
                now,
                JsonSerializer.SerializeToElement(new { kind = "clock_jump", localDate = today })))
            .ToList());
        var fiftyResponse = await desktop.PostAsJsonAsync("/v1/sync/events", exactlyFifty);
        Assert.Equal(HttpStatusCode.OK, fiftyResponse.StatusCode);

        var unknownField = await PostBatchAsync(desktop, new SyncEventDto(
            "unknown-field-1",
            SyncEventType.ProtectionOff,
            now,
            JsonSerializer.SerializeToElement(new
            {
                source = "parent",
                localDate = today,
                url = "https://secret.example.com",
                windowTitle = "Math homework",
            })));
        Assert.Equal(SyncEventResultStatus.Rejected, unknownField[0].Status);
        Assert.Equal("unknown_field", unknownField[0].Code);

        await using (var db = CreateContext(connectionString))
        {
            var rejected = await db.SyncEvents.AsNoTracking()
                .SingleAsync(e => e.ClientEventId == "unknown-field-1");
            Assert.Equal(EntitySyncEventStatus.Rejected, rejected.Status);
            Assert.Equal(50, await db.SyncEvents.CountAsync(e => e.ClientEventId.StartsWith("alert-")));
        }

        var oversizedPayload = JsonSerializer.SerializeToElement(new
        {
            source = "parent",
            localDate = today,
            padding = new string('a', 70_000),
        });
        var oversizedResponse = await desktop.PostAsJsonAsync(
            "/v1/sync/events",
            new SyncBatchRequest([
                new SyncEventDto("big-1", SyncEventType.ProtectionOff, now, oversizedPayload),
            ]));
        Assert.Equal(HttpStatusCode.RequestEntityTooLarge, oversizedResponse.StatusCode);
        var oversizedError = await oversizedResponse.Content.ReadFromJsonAsync<EduFocus.Cloud.Contracts.Common.ApiErrorResponse>(Json);
        Assert.Equal("payload_too_large", oversizedError!.Error.Code);
    }

    private static async Task<IReadOnlyList<SyncEventResult>> PostBatchAsync(
        HttpClient client,
        params SyncEventDto[] events)
    {
        var response = await client.PostAsJsonAsync("/v1/sync/events", new SyncBatchRequest(events));
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<SyncBatchResponse>(Json);
        Assert.NotNull(body);
        return body.Results;
    }

    private static JsonElement SummaryPayload(
        DateOnly localDate,
        DateTimeOffset generatedAtUtc,
        long studySeconds) =>
        JsonSerializer.SerializeToElement(new
        {
            localDate = localDate.ToString("yyyy-MM-dd"),
            timeZoneId = "Asia/Ho_Chi_Minh",
            generatedAtUtc,
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
            attentionPercent = 85.7,
            coveragePercent = 90.0,
        });

    private static DateOnly LocalToday()
    {
        var timeZone = TimeZoneInfo.FindSystemTimeZoneById("Asia/Ho_Chi_Minh");
        return DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(DateTimeOffset.UtcNow, timeZone).DateTime);
    }

    private static async Task<string> CreateDesktopTokenAsync(HttpClient anonymous)
    {
        var response = await anonymous.PostAsJsonAsync(
            "/v1/pairing/sessions",
            new CreatePairingSessionRequest("Student PC", "windows"));
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var session = await response.Content.ReadFromJsonAsync<PairingSessionResponse>(Json);
        return session!.DesktopToken;
    }

    private static HttpClient CreateAuthenticatedClient(CloudApiFactory factory, string token)
    {
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return client;
    }

    private static async Task ResetDatabaseAsync(string connectionString)
    {
        await using var db = CreateContext(connectionString);
        await db.Database.EnsureDeletedAsync();
        await db.Database.MigrateAsync();
    }

    private static CloudDbContext CreateContext(string connectionString) =>
        new(new DbContextOptionsBuilder<CloudDbContext>().UseNpgsql(connectionString).Options);
}
