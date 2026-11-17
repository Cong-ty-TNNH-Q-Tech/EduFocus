using System.Globalization;

namespace EduFocus.Parent.Core.Push;

public enum PushNotificationType
{
    DailySummary,
    AttentionAlert,
    ProtectionOff,
    SecurityAlert,
}

public sealed record PushNotificationPayload(
    PushNotificationType Type,
    string TypeKey,
    DateOnly? LocalDate,
    string DedupeKey);

/// <summary>
/// Backend sends data-only FCM messages with exactly the keys in
/// <c>PushRules.BuildPushData</c>: type, date (yyyy-MM-dd) and notificationId.
/// Anything else (unknown type, control characters) is dropped before it can
/// reach a notification or a log.
/// </summary>
public static class PushNotificationPayloadParser
{
    public const string TypeKeyName = "type";
    public const string DateKeyName = "date";
    public const string NotificationIdKeyName = "notificationId";
    private const int MaxNotificationIdLength = 64;

    public static bool TryParse(
        IReadOnlyDictionary<string, string>? data,
        out PushNotificationPayload? payload)
    {
        payload = null;
        if (data is null || !data.TryGetValue(TypeKeyName, out var rawType))
            return false;

        if (!TryParseType(rawType, out var type))
            return false;

        DateOnly? localDate = null;
        if (data.TryGetValue(DateKeyName, out var rawDate)
            && DateOnly.TryParseExact(
                rawDate?.Trim(),
                "yyyy-MM-dd",
                CultureInfo.InvariantCulture,
                DateTimeStyles.None,
                out var parsedDate))
        {
            localDate = parsedDate;
        }

        var notificationId = NormalizeNotificationId(
            data.TryGetValue(NotificationIdKeyName, out var rawId) ? rawId : null);
        var dedupeKey = notificationId is null
            ? $"{TypeKeyOf(type)}:{localDate?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) ?? "unknown"}"
            : $"{TypeKeyOf(type)}:{notificationId}";

        payload = new PushNotificationPayload(type, TypeKeyOf(type), localDate, dedupeKey);
        return true;
    }

    public static bool TryParseType(string? value, out PushNotificationType type)
    {
        switch (value?.Trim().ToLowerInvariant())
        {
            case "daily_summary":
                type = PushNotificationType.DailySummary;
                return true;
            case "attention_alert":
                type = PushNotificationType.AttentionAlert;
                return true;
            case "protection_off":
                type = PushNotificationType.ProtectionOff;
                return true;
            case "security_alert":
                type = PushNotificationType.SecurityAlert;
                return true;
            default:
                type = default;
                return false;
        }
    }

    public static string TypeKeyOf(PushNotificationType type) => type switch
    {
        PushNotificationType.DailySummary => "daily_summary",
        PushNotificationType.AttentionAlert => "attention_alert",
        PushNotificationType.ProtectionOff => "protection_off",
        PushNotificationType.SecurityAlert => "security_alert",
        _ => throw new ArgumentOutOfRangeException(nameof(type)),
    };

    private static string? NormalizeNotificationId(string? value)
    {
        var trimmed = value?.Trim();
        if (string.IsNullOrEmpty(trimmed)
            || trimmed.Length > MaxNotificationIdLength
            || trimmed.Any(ch => char.IsControl(ch) || ch is '|' or '\n'))
        {
            return null;
        }

        return trimmed;
    }
}
