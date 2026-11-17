namespace EduFocus.Cloud.Data.Entities;

public enum NotificationCategory
{
    DailySummary,
    AttentionAlert,
    ProtectionOff,
    SecurityAlert,
}

public enum NotificationStatus
{
    Pending,
    Sent,
    Suppressed,
    Failed,
}

public sealed class NotificationRecord
{
    public Guid Id { get; set; }
    public Guid FamilyId { get; set; }
    public NotificationCategory Category { get; set; }
    public string DedupeKey { get; set; } = string.Empty;
    public DateOnly? LocalDate { get; set; }
    public DateTimeOffset CreatedAtUtc { get; set; }
    public DateTimeOffset? SentAtUtc { get; set; }
    public NotificationStatus Status { get; set; } = NotificationStatus.Pending;
    public string? FailureCode { get; set; }
}
