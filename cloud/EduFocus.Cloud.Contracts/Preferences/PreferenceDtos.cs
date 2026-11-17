namespace EduFocus.Cloud.Contracts.Preferences;

public sealed record NotificationPreferencesDto(
    bool AttentionAlert,
    bool DailySummary,
    bool ProtectionOff,
    bool SecurityAlert,
    int QuietStartMinutes,
    int QuietEndMinutes,
    int DailySummaryMinutes,
    string TimeZoneId,
    bool SecurityAlertBypassQuietHours = true);

public sealed record UpdateNotificationPreferencesRequest(
    bool AttentionAlert,
    bool DailySummary,
    bool ProtectionOff,
    bool SecurityAlert,
    int QuietStartMinutes,
    int QuietEndMinutes,
    int DailySummaryMinutes,
    string TimeZoneId,
    bool SecurityAlertBypassQuietHours = true);
