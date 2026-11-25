using EduFocus.Cloud.Api.Push;
using EduFocus.Cloud.Data.Entities;

namespace EduFocus.Cloud.Tests;

public sealed class PushRulesTests
{
    private const string TimeZone = "Asia/Ho_Chi_Minh";

    [Theory]
    [InlineData("2026-11-17T15:00:00Z", true)]
    [InlineData("2026-11-17T16:30:00Z", true)]
    [InlineData("2026-11-17T22:59:00Z", true)]
    [InlineData("2026-11-17T14:59:00Z", false)]
    [InlineData("2026-11-17T23:00:00Z", false)]
    [InlineData("2026-11-17T05:00:00Z", false)]
    public void Quiet_hours_handle_overnight_window(string nowUtc, bool expected)
    {
        var quiet = PushRules.IsQuietHours(
            DateTimeOffset.Parse(nowUtc),
            TimeZone,
            22 * 60,
            6 * 60);

        Assert.Equal(expected, quiet);
    }

    [Theory]
    [InlineData("2026-11-17T05:00:00Z", true)]
    [InlineData("2026-11-17T06:30:00Z", true)]
    [InlineData("2026-11-17T06:59:00Z", true)]
    [InlineData("2026-11-17T07:00:00Z", false)]
    [InlineData("2026-11-17T04:59:00Z", false)]
    public void Quiet_hours_handle_daytime_window(string nowUtc, bool expected)
    {
        var quiet = PushRules.IsQuietHours(
            DateTimeOffset.Parse(nowUtc),
            TimeZone,
            12 * 60,
            14 * 60);

        Assert.Equal(expected, quiet);
    }

    [Fact]
    public void Zero_length_window_is_never_quiet()
    {
        var quiet = PushRules.IsQuietHours(
            DateTimeOffset.Parse("2026-11-17T16:30:00Z"),
            TimeZone,
            22 * 60,
            22 * 60);

        Assert.False(quiet);
    }

    [Fact]
    public void Retry_backoff_grows_and_is_capped()
    {
        Assert.Equal(TimeSpan.FromMinutes(2), PushRules.RetryBackoff(1));
        Assert.Equal(TimeSpan.FromMinutes(4), PushRules.RetryBackoff(2));
        Assert.True(PushRules.RetryBackoff(20) <= TimeSpan.FromMinutes(10));
    }

    [Fact]
    public void Type_names_match_the_contract_values()
    {
        Assert.Equal("attention_alert", PushRules.TypeName(NotificationCategory.AttentionAlert));
        Assert.Equal("blocked_attempt", PushRules.TypeName(NotificationCategory.BlockedAttempt));
    }

    [Fact]
    public void Push_payload_contains_only_type_date_and_notification_id()
    {
        var notification = new NotificationRecord
        {
            Id = Guid.Parse("6f1c2d3e-4a5b-4c6d-8e7f-901234567890"),
            Category = NotificationCategory.AttentionAlert,
            LocalDate = new DateOnly(2026, 11, 17),
        };

        var data = PushRules.BuildPushData(notification);

        Assert.Equal(3, data.Count);
        Assert.Equal("attention_alert", data["type"]);
        Assert.Equal("2026-11-17", data["date"]);
        Assert.Equal(notification.Id.ToString(), data["notificationId"]);
        Assert.DoesNotContain(
            data.Keys,
            key => key is "title" or "body" or "studentName" or "url");
    }

    [Fact]
    public void Preference_toggles_map_to_categories()
    {
        var preferences = new NotificationPreference
        {
            AttentionAlert = false,
            DailySummary = true,
            ProtectionOff = false,
            SecurityAlert = true,
            BlockedAttempt = false,
            SecurityAlertBypassQuietHours = false,
        };

        Assert.False(PushRules.IsEnabled(preferences, NotificationCategory.AttentionAlert));
        Assert.True(PushRules.IsEnabled(preferences, NotificationCategory.DailySummary));
        Assert.False(PushRules.IsEnabled(preferences, NotificationCategory.ProtectionOff));
        Assert.True(PushRules.IsEnabled(preferences, NotificationCategory.SecurityAlert));
        Assert.False(PushRules.IsEnabled(preferences, NotificationCategory.BlockedAttempt));
        Assert.False(PushRules.BypassesQuietHours(preferences, NotificationCategory.SecurityAlert));
        Assert.False(PushRules.BypassesQuietHours(preferences, NotificationCategory.AttentionAlert));
    }

    [Fact]
    public void Missing_preferences_enable_all_categories()
    {
        Assert.True(PushRules.IsEnabled(null, NotificationCategory.DailySummary));
        Assert.True(PushRules.IsEnabled(null, NotificationCategory.AttentionAlert));
        Assert.True(PushRules.IsEnabled(null, NotificationCategory.ProtectionOff));
        Assert.True(PushRules.IsEnabled(null, NotificationCategory.SecurityAlert));
        Assert.True(PushRules.IsEnabled(null, NotificationCategory.BlockedAttempt));
        Assert.True(PushRules.BypassesQuietHours(null, NotificationCategory.SecurityAlert));
    }
}
