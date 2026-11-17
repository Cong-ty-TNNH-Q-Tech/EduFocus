namespace EduFocus.Cloud.Api.Options;

public sealed class JobOptions
{
    public const string SectionName = "Jobs";

    public bool Enabled { get; set; } = true;
    public int IntervalSeconds { get; set; } = 60;
    public int PairingSessionHours { get; set; } = 24;
    public int SyncEventDays { get; set; } = 7;
    public int NotificationDays { get; set; } = 30;
    public int DailySummaryDays { get; set; } = 180;
    public int CloudAuditDays { get; set; } = 365;
    public int SoftDeletedFamilyDays { get; set; } = 30;
}
