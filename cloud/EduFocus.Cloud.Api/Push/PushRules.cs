using EduFocus.Cloud.Data.Entities;

namespace EduFocus.Cloud.Api.Push;

public static class PushRules
{
    public const int MinutesPerDay = 24 * 60;
    private static readonly TimeSpan MaxBackoff = TimeSpan.FromMinutes(10);

    public static int LocalMinutes(DateTimeOffset nowUtc, string timeZoneId)
    {
        var zone = TimeZoneInfo.TryFindSystemTimeZoneById(timeZoneId, out var found)
            ? found
            : TimeZoneInfo.Utc;
        var local = TimeZoneInfo.ConvertTime(nowUtc, zone);
        return (local.Hour * 60) + local.Minute;
    }

    public static bool IsQuietHours(
        DateTimeOffset nowUtc,
        string timeZoneId,
        int quietStartMinutes,
        int quietEndMinutes)
    {
        if (quietStartMinutes == quietEndMinutes)
        {
            return false;
        }

        var minutes = LocalMinutes(nowUtc, timeZoneId);
        return quietStartMinutes < quietEndMinutes
            ? minutes >= quietStartMinutes && minutes < quietEndMinutes
            : minutes >= quietStartMinutes || minutes < quietEndMinutes;
    }

    public static TimeSpan RetryBackoff(int attemptCount)
    {
        var minutes = Math.Min(Math.Max(attemptCount, 1) * 2, (int)MaxBackoff.TotalMinutes);
        return TimeSpan.FromMinutes(minutes);
    }

    public static string TypeName(NotificationCategory category) => category switch
    {
        NotificationCategory.DailySummary => "daily_summary",
        NotificationCategory.AttentionAlert => "attention_alert",
        NotificationCategory.ProtectionOff => "protection_off",
        NotificationCategory.SecurityAlert => "security_alert",
        _ => throw new ArgumentOutOfRangeException(nameof(category)),
    };

    public static bool IsEnabled(NotificationPreference? preferences, NotificationCategory category) =>
        category switch
        {
            NotificationCategory.DailySummary => preferences?.DailySummary ?? true,
            NotificationCategory.AttentionAlert => preferences?.AttentionAlert ?? true,
            NotificationCategory.ProtectionOff => preferences?.ProtectionOff ?? true,
            NotificationCategory.SecurityAlert => preferences?.SecurityAlert ?? true,
            _ => false,
        };

    public static bool BypassesQuietHours(NotificationPreference? preferences, NotificationCategory category) =>
        category == NotificationCategory.SecurityAlert
        && (preferences?.SecurityAlertBypassQuietHours ?? true);

    public static IReadOnlyDictionary<string, string> BuildPushData(NotificationRecord notification) =>
        new Dictionary<string, string>
        {
            ["type"] = TypeName(notification.Category),
            ["date"] = (notification.LocalDate ?? DateOnly.FromDateTime(DateTime.UtcNow))
                .ToString("yyyy-MM-dd"),
            ["notificationId"] = notification.Id.ToString(),
        };
}
