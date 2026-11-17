namespace EduFocus.Cloud.Data.Entities;

public enum SyncEventType
{
    DailySummary,
    AttentionAlert,
    ProtectionOff,
    SecurityAlert,
}

public enum SyncEventStatus
{
    Pending,
    Processed,
    Rejected,
}

public sealed class SyncEvent
{
    public Guid Id { get; set; }
    public Guid FamilyId { get; set; }
    public string ClientEventId { get; set; } = string.Empty;
    public SyncEventType Type { get; set; }
    public DateTimeOffset ReceivedAtUtc { get; set; }
    public DateTimeOffset? ProcessedAtUtc { get; set; }
    public SyncEventStatus Status { get; set; } = SyncEventStatus.Pending;
}
