namespace EduFocus.Cloud.Contracts.Preferences;

public sealed record NotificationPreferencesDto(
    TimeOnly QuietStart,
    TimeOnly QuietEnd,
    TimeOnly DailySummaryTime,
    bool AttentionAlertsEnabled,
    bool ProtectionOffEnabled,
    bool SummaryEnabled,
    bool SecurityAlertBypassQuietHours);

public sealed record UpdateNotificationPreferencesRequest(
    TimeOnly QuietStart,
    TimeOnly QuietEnd,
    TimeOnly DailySummaryTime,
    bool AttentionAlertsEnabled,
    bool ProtectionOffEnabled,
    bool SummaryEnabled,
    bool SecurityAlertBypassQuietHours);
