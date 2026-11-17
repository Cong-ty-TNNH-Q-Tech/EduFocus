using System.Globalization;

namespace EduFocus.Parent.Core.Push;

/// <summary>
/// Maps a tapped notification to a Shell route: summary/attention open the
/// daily report (with the local date when present), protection/security open
/// the notifications tab.
/// </summary>
public static class PushTapRoute
{
    public static bool TryCreate(string? typeKey, string? localDate, out string? route)
    {
        route = null;
        if (!PushNotificationPayloadParser.TryParseType(typeKey, out var type))
            return false;

        DateOnly? date = null;
        if (!string.IsNullOrWhiteSpace(localDate)
            && DateOnly.TryParseExact(
                localDate.Trim(),
                "yyyy-MM-dd",
                CultureInfo.InvariantCulture,
                DateTimeStyles.None,
                out var parsedDate))
        {
            date = parsedDate;
        }

        route = type switch
        {
            PushNotificationType.DailySummary or PushNotificationType.AttentionAlert
                => date is { } value
                    ? $"//reports?date={value.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)}"
                    : "//reports",
            PushNotificationType.ProtectionOff or PushNotificationType.SecurityAlert
                => "//notifications",
            _ => null,
        };

        return route is not null;
    }
}
