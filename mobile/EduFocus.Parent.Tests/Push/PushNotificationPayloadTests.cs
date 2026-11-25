using EduFocus.Parent.Core.Push;

namespace EduFocus.Parent.Tests.Push;

public sealed class PushNotificationPayloadTests
{
    private const string NotificationId = "0f4a8f0e-6a70-4b03-b86a-6f2d6a12f3b8";

    [Theory]
    [InlineData("daily_summary", PushNotificationType.DailySummary)]
    [InlineData("attention_alert", PushNotificationType.AttentionAlert)]
    [InlineData("protection_off", PushNotificationType.ProtectionOff)]
    [InlineData("security_alert", PushNotificationType.SecurityAlert)]
    [InlineData("blocked_attempt", PushNotificationType.BlockedAttempt)]
    public void Parses_all_backend_types(string rawType, PushNotificationType expected)
    {
        var parsed = PushNotificationPayloadParser.TryParse(
            Data(rawType, "2026-11-17", NotificationId),
            out var payload);

        Assert.True(parsed);
        Assert.NotNull(payload);
        Assert.Equal(expected, payload!.Type);
        Assert.Equal(rawType, payload.TypeKey);
        Assert.Equal(new DateOnly(2026, 11, 17), payload.LocalDate);
        Assert.Equal($"{rawType}:{NotificationId}", payload.DedupeKey);
    }

    [Fact]
    public void Accepts_uppercase_type_keys()
    {
        var parsed = PushNotificationPayloadParser.TryParse(
            Data("Daily_Summary", null, null),
            out var payload);

        Assert.True(parsed);
        Assert.Equal(PushNotificationType.DailySummary, payload!.Type);
    }

    [Fact]
    public void Rejects_unknown_or_missing_type()
    {
        Assert.False(PushNotificationPayloadParser.TryParse(
            Data("app_installed", "2026-11-17", NotificationId),
            out var unknown));
        Assert.Null(unknown);

        Assert.False(PushNotificationPayloadParser.TryParse(
            new Dictionary<string, string> { ["date"] = "2026-11-17" },
            out var missing));
        Assert.Null(missing);

        Assert.False(PushNotificationPayloadParser.TryParse(null, out var none));
        Assert.Null(none);
    }

    [Fact]
    public void Invalid_date_is_dropped_but_notification_is_kept()
    {
        var parsed = PushNotificationPayloadParser.TryParse(
            Data("daily_summary", "17/11/2026", NotificationId),
            out var payload);

        Assert.True(parsed);
        Assert.Null(payload!.LocalDate);
        Assert.Equal($"daily_summary:{NotificationId}", payload.DedupeKey);
    }

    [Fact]
    public void Missing_notification_id_falls_back_to_type_and_date()
    {
        var parsed = PushNotificationPayloadParser.TryParse(
            Data("protection_off", "2026-11-17", null),
            out var payload);

        Assert.True(parsed);
        Assert.Equal("protection_off:2026-11-17", payload!.DedupeKey);
    }

    [Fact]
    public void Unsafe_notification_id_falls_back_to_date()
    {
        foreach (var unsafeId in new[] { "abc|def", "abc\ndef", "abc\u0007def", new string('x', 65) })
        {
            var parsed = PushNotificationPayloadParser.TryParse(
                Data("security_alert", "2026-11-17", unsafeId),
                out var payload);

            Assert.True(parsed);
            Assert.Equal("security_alert:2026-11-17", payload!.DedupeKey);
        }
    }

    private static Dictionary<string, string> Data(string? type, string? date, string? notificationId)
    {
        var data = new Dictionary<string, string>();
        if (type is not null)
            data["type"] = type;
        if (date is not null)
            data["date"] = date;
        if (notificationId is not null)
            data["notificationId"] = notificationId;
        return data;
    }
}
