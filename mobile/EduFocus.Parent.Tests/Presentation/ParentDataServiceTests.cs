using System.Net;
using System.Text.Json;
using EduFocus.Cloud.Contracts.Preferences;
using EduFocus.Parent.Core;
using EduFocus.Parent.Core.Presentation;
using EduFocus.Parent.Core.Services;
using EduFocus.Parent.Tests.TestSupport;

namespace EduFocus.Parent.Tests.Presentation;

public sealed class ParentDataServiceTests
{
    private const string DayJson = """
        [{"localDate":"2026-11-17","timeZoneId":"Asia/Ho_Chi_Minh",
          "generatedAtUtc":"2026-11-17T10:30:00+00:00","sessionsCount":3,
          "activeSeconds":9000,"studySeconds":8100,"idleSeconds":600,
          "onlineClassSeconds":1800,"monitoringEligibleSeconds":7200,
          "monitoredSeconds":6900,"focusedSeconds":5700,"distractedSeconds":900,
          "absentSeconds":300,"drowsySeconds":100,"blockedAttempts":2,
          "attentionPercent":82.5,"coveragePercent":96}]
        """;

    [Fact]
    public async Task Get_daily_builds_range_query()
    {
        var (service, handler) = Create(_ => HttpStubs.Json(HttpStatusCode.OK, DayJson));

        var days = await service.GetDailyAsync(new DateOnly(2026, 11, 11), new DateOnly(2026, 11, 17));

        var request = Assert.Single(handler.Requests);
        Assert.Equal(HttpMethod.Get, request.Method);
        Assert.Equal(
            "https://cloud.example.com/v1/reports/daily?from=2026-11-11&to=2026-11-17",
            request.RequestUri?.ToString());
        var day = Assert.Single(days);
        Assert.Equal(8100, day.StudySeconds);
        Assert.Equal(82.5, day.AttentionPercent);
    }

    [Fact]
    public async Task Notification_cursor_is_url_escaped()
    {
        var (service, handler) = Create(_ => HttpStubs.Json(
            HttpStatusCode.OK,
            """{"items":[],"nextBefore":null}"""));

        await service.GetNotificationsAsync(50, "cursor+/=");

        Assert.Equal(
            "https://cloud.example.com/v1/notifications?limit=50&before=cursor%2B%2F%3D",
            handler.Requests[0].RequestUri?.ToString());
    }

    [Fact]
    public async Task Update_preferences_uses_put_with_body()
    {
        var (service, handler) = Create(_ => HttpStubs.Json(
            HttpStatusCode.OK,
            """
            {"attentionAlert":false,"dailySummary":true,"protectionOff":true,
             "securityAlert":true,"quietStartMinutes":1320,"quietEndMinutes":360,
             "dailySummaryMinutes":1230,"timeZoneId":"Asia/Ho_Chi_Minh",
             "securityAlertBypassQuietHours":true}
            """));

        var updated = await service.UpdatePreferencesAsync(new UpdateNotificationPreferencesRequest(
            false, true, true, true, 1320, 360, 1230, "Asia/Ho_Chi_Minh"));

        var request = Assert.Single(handler.Requests);
        Assert.Equal(HttpMethod.Put, request.Method);
        Assert.Equal("https://cloud.example.com/v1/preferences", request.RequestUri?.ToString());
        Assert.Contains("\"attentionAlert\":false", handler.RequestBodies[0]);
        Assert.False(updated.AttentionAlert);
    }

    [Fact]
    public async Task Get_preferences_reads_defaults()
    {
        var (service, handler) = Create(_ => HttpStubs.Json(
            HttpStatusCode.OK,
            """
            {"attentionAlert":true,"dailySummary":true,"protectionOff":true,
             "securityAlert":true,"quietStartMinutes":1320,"quietEndMinutes":360,
             "dailySummaryMinutes":1230,"timeZoneId":"Asia/Ho_Chi_Minh",
             "securityAlertBypassQuietHours":true}
            """));

        var preferences = await service.GetPreferencesAsync();

        Assert.Equal("/v1/preferences", handler.Requests[0].RequestUri?.AbsolutePath);
        Assert.Equal(1320, preferences.QuietStartMinutes);
        Assert.Equal("Asia/Ho_Chi_Minh", preferences.TimeZoneId);
    }

    private static (ParentDataService Service, RecordingHttpMessageHandler Handler) Create(
        Func<HttpRequestMessage, HttpResponseMessage> responder)
    {
        var handler = new RecordingHttpMessageHandler(responder);
        var state = new AppState { CloudBaseUrl = "https://cloud.example.com" };
        var api = new CloudApiClient(
            new RecordingHttpClientFactory(handler),
            new MemoryTokenStore("efm_token"),
            state);
        return (new ParentDataService(api), handler);
    }
}
