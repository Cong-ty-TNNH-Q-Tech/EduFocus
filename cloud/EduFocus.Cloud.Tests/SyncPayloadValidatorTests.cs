using System.Text.Json;
using EduFocus.Cloud.Api.Sync;
using EduFocus.Cloud.Contracts.Sync;

namespace EduFocus.Cloud.Tests;

public sealed class SyncPayloadValidatorTests
{
    [Theory]
    [InlineData(
        """{"localDate":"2026-11-17","timeZoneId":"Asia/Ho_Chi_Minh","generatedAtUtc":"2026-11-17T13:30:00Z","sessionsCount":2,"activeSeconds":7200,"studySeconds":6500,"idleSeconds":700,"onlineClassSeconds":3600,"monitoringEligibleSeconds":7000,"monitoredSeconds":6300,"focusedSeconds":5400,"distractedSeconds":600,"absentSeconds":240,"drowsySeconds":60,"blockedAttempts":12,"attentionPercent":85.7,"coveragePercent":90}""")]
    [InlineData(
        """{"localDate":"2026-11-17","generatedAtUtc":"2026-11-17T13:30:00Z"}""")]
    public void Valid_daily_summary_is_accepted(string payload)
    {
        var result = SyncPayloadValidator.Validate(Event(SyncEventType.DailySummary, payload));

        Assert.True(result.IsValid);
        Assert.NotNull(result.Value!.DailySummary);
    }

    [Fact]
    public void Unknown_payload_field_is_rejected()
    {
        var result = SyncPayloadValidator.Validate(Event(
            SyncEventType.DailySummary,
            """
            {"localDate":"2026-11-17","generatedAtUtc":"2026-11-17T13:30:00Z",
             "url":"https://secret.example.com","windowTitle":"Math homework"}
            """));

        Assert.False(result.IsValid);
        Assert.Equal("unknown_field", result.Code);
    }

    [Theory]
    [InlineData("\"activeSeconds\":86401")]
    [InlineData("\"studySeconds\":-1")]
    [InlineData("\"blockedAttempts\":-2")]
    [InlineData("\"attentionPercent\":101")]
    [InlineData("\"coveragePercent\":-1")]
    public void Out_of_range_daily_summary_fields_are_rejected(string field)
    {
        var result = SyncPayloadValidator.Validate(Event(
            SyncEventType.DailySummary,
            $$"""{"localDate":"2026-11-17","generatedAtUtc":"2026-11-17T13:30:00Z",{{field}}}"""));

        Assert.False(result.IsValid);
        Assert.Equal("invalid_payload", result.Code);
    }

    [Fact]
    public void Missing_required_field_is_rejected()
    {
        var result = SyncPayloadValidator.Validate(Event(
            SyncEventType.DailySummary,
            """{"generatedAtUtc":"2026-11-17T13:30:00Z"}"""));

        Assert.False(result.IsValid);
        Assert.Equal("invalid_payload", result.Code);
    }

    [Fact]
    public void Invalid_time_zone_is_rejected()
    {
        var result = SyncPayloadValidator.Validate(Event(
            SyncEventType.DailySummary,
            """
            {"localDate":"2026-11-17","timeZoneId":"Mars/Olympus",
             "generatedAtUtc":"2026-11-17T13:30:00Z"}
            """));

        Assert.False(result.IsValid);
        Assert.Equal("invalid_time_zone", result.Code);
    }

    [Theory]
    [InlineData(59, false)]
    [InlineData(60, true)]
    [InlineData(420, true)]
    public void Attention_alert_duration_is_validated(int durationSeconds, bool expectedValid)
    {
        var result = SyncPayloadValidator.Validate(Event(
            SyncEventType.AttentionAlert,
            $$"""
            {"kind":"distracted","localDate":"2026-11-17",
             "startedAtUtc":"2026-11-17T09:05:00Z","durationSeconds":{{durationSeconds}}}
            """));

        Assert.Equal(expectedValid, result.IsValid);
        if (!expectedValid)
        {
            Assert.Equal("invalid_payload", result.Code);
        }
    }

    [Fact]
    public void Unknown_attention_kind_is_rejected()
    {
        var result = SyncPayloadValidator.Validate(Event(
            SyncEventType.AttentionAlert,
            """
            {"kind":"sleepy","localDate":"2026-11-17",
             "startedAtUtc":"2026-11-17T09:05:00Z","durationSeconds":300}
            """));

        Assert.False(result.IsValid);
        Assert.Equal("invalid_payload", result.Code);
    }

    [Theory]
    [InlineData("""{"source":"parent","localDate":"2026-11-17"}""", SyncEventType.ProtectionOff)]
    [InlineData("""{"source":"exit","localDate":"2026-11-17"}""", SyncEventType.ProtectionOff)]
    [InlineData("""{"kind":"clock_jump","localDate":"2026-11-17"}""", SyncEventType.SecurityAlert)]
    public void Alert_payloads_are_accepted(string payload, SyncEventType type)
    {
        var result = SyncPayloadValidator.Validate(Event(type, payload));

        Assert.True(result.IsValid);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Blank_client_event_id_is_rejected(string clientEventId)
    {
        var result = SyncPayloadValidator.Validate(Event(
            SyncEventType.SecurityAlert,
            """{"kind":"clock_jump","localDate":"2026-11-17"}""",
            clientEventId));

        Assert.False(result.IsValid);
        Assert.Equal("invalid_event", result.Code);
    }

    [Fact]
    public void Client_event_id_longer_than_limit_is_rejected()
    {
        var result = SyncPayloadValidator.Validate(Event(
            SyncEventType.SecurityAlert,
            """{"kind":"clock_jump","localDate":"2026-11-17"}""",
            new string('a', 65)));

        Assert.False(result.IsValid);
        Assert.Equal("invalid_event", result.Code);
    }

    [Fact]
    public void Non_object_payload_is_rejected()
    {
        var result = SyncPayloadValidator.Validate(Event(SyncEventType.ProtectionOff, "\"parent\""));

        Assert.False(result.IsValid);
        Assert.Equal("invalid_payload", result.Code);
    }

    private static SyncEventDto Event(
        SyncEventType type,
        string payloadJson,
        string clientEventId = "event-1") =>
        new(
            clientEventId,
            type,
            DateTimeOffset.Parse("2026-11-17T13:30:00Z"),
            JsonDocument.Parse(payloadJson).RootElement.Clone());
}
