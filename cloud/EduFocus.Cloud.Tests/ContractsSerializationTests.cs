using System.Text.Json;
using EduFocus.Cloud.Contracts.Json;
using EduFocus.Cloud.Contracts.Notifications;
using EduFocus.Cloud.Contracts.Pairing;
using EduFocus.Cloud.Contracts.Preferences;
using EduFocus.Cloud.Contracts.Sync;

namespace EduFocus.Cloud.Tests;

public sealed class ContractsSerializationTests
{
    [Fact]
    public void Pairing_response_uses_camel_case()
    {
        var response = new PairingSessionResponse(
            Guid.Parse("6f1c2d3e-4a5b-4c6d-8e7f-901234567890"),
            "ABCD2345",
            "edufocus://pair?v=1&u=aHR0cHM6Ly9jbG91ZC5leGFtcGxl&s=6f1c&c=ABCD2345",
            DateTimeOffset.Parse("2026-11-17T10:00:00Z"),
            "efd_test-token",
            "https://cloud.example.com");

        var json = JsonSerializer.Serialize(response, ApiJson.Options);

        Assert.Contains("\"sessionId\"", json);
        Assert.Contains("\"pairingUri\"", json);
        Assert.Contains("\"desktopToken\"", json);
        Assert.Contains("\"cloudBaseUrl\"", json);
        Assert.DoesNotContain("\"SessionId\"", json);
    }

    [Fact]
    public void Sync_event_type_uses_snake_case_values()
    {
        var payload = JsonSerializer.SerializeToElement(
            new DailySummaryPayload(
                new DateOnly(2026, 11, 17),
                "Asia/Ho_Chi_Minh",
                DateTimeOffset.Parse("2026-11-17T13:30:00Z"),
                1,
                5700,
                5640,
                0,
                2700,
                5640,
                5000,
                4300,
                600,
                120,
                60,
                12,
                88,
                92),
            ApiJson.Options);

        var syncEvent = new SyncEventDto(
            "audit-1234",
            SyncEventType.DailySummary,
            DateTimeOffset.Parse("2026-11-17T13:30:00Z"),
            payload);

        var json = JsonSerializer.Serialize(syncEvent, ApiJson.Options);

        Assert.Contains("\"type\":\"daily_summary\"", json);
        Assert.Contains("\"generatedAtUtc\"", json);
        Assert.Contains("\"clientEventId\"", json);
        Assert.Equal(
            "\"blocked_attempt\"",
            JsonSerializer.Serialize(SyncEventType.BlockedAttempt, ApiJson.Options));
    }

    [Fact]
    public void Attention_alert_kind_uses_contract_value()
    {
        var json = JsonSerializer.Serialize(AttentionAlertKind.Absent, ApiJson.Options);

        Assert.Equal("\"absent\"", json);
    }

    [Fact]
    public void Notification_kind_uses_contract_value()
    {
        var json = JsonSerializer.Serialize(NotificationKind.ProtectionOff, ApiJson.Options);

        Assert.Equal("\"protection_off\"", json);
        Assert.Equal(
            "\"blocked_attempt\"",
            JsonSerializer.Serialize(NotificationKind.BlockedAttempt, ApiJson.Options));
    }

    [Fact]
    public void Preferences_round_trip_uses_minute_fields()
    {
        var preferences = new NotificationPreferencesDto(
            true,
            true,
            true,
            true,
            1320,
            360,
            1230,
            "Asia/Ho_Chi_Minh");

        var json = JsonSerializer.Serialize(preferences, ApiJson.Options);
        var roundTrip = JsonSerializer.Deserialize<NotificationPreferencesDto>(json, ApiJson.Options);

        Assert.Equal(preferences, roundTrip);
        Assert.Contains("\"quietStartMinutes\":1320", json);
        Assert.Contains("\"dailySummaryMinutes\":1230", json);
        Assert.Contains("\"securityAlertBypassQuietHours\":true", json);
        Assert.Contains("\"blockedAttempt\":true", json);
    }
}
