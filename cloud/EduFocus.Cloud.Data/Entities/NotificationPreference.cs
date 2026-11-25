namespace EduFocus.Cloud.Data.Entities;

public sealed class NotificationPreference
{
    public Guid FamilyId { get; set; }
    public bool AttentionAlert { get; set; } = true;
    public bool DailySummary { get; set; } = true;
    public bool ProtectionOff { get; set; } = true;
    public bool SecurityAlert { get; set; } = true;
    public bool BlockedAttempt { get; set; } = true;
    public bool SecurityAlertBypassQuietHours { get; set; } = true;
    public int QuietStartMinutes { get; set; } = 22 * 60;
    public int QuietEndMinutes { get; set; } = 6 * 60;
    public int DailySummaryMinutes { get; set; } = 20 * 60 + 30;
    public string TimeZoneId { get; set; } = "Asia/Ho_Chi_Minh";
    public DateTimeOffset UpdatedAtUtc { get; set; }
}
