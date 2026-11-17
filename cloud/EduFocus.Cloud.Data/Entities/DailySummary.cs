namespace EduFocus.Cloud.Data.Entities;

public sealed class DailySummary
{
    public Guid Id { get; set; }
    public Guid FamilyId { get; set; }
    public Guid DesktopDeviceId { get; set; }
    public DateOnly LocalDate { get; set; }
    public int SessionsCount { get; set; }
    public long ActiveSeconds { get; set; }
    public long StudySeconds { get; set; }
    public long IdleSeconds { get; set; }
    public long OnlineClassSeconds { get; set; }
    public long MonitoringEligibleSeconds { get; set; }
    public long MonitoredSeconds { get; set; }
    public long FocusedSeconds { get; set; }
    public long DistractedSeconds { get; set; }
    public long AbsentSeconds { get; set; }
    public long DrowsySeconds { get; set; }
    public int BlockedAttempts { get; set; }
    public int? AttentionPercent { get; set; }
    public int? CoveragePercent { get; set; }
    public DateTimeOffset UpdatedAtUtc { get; set; }
}
