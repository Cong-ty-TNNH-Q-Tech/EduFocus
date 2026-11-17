namespace EduFocus.Cloud.Data.Entities;

public sealed class CloudAuditLog
{
    public Guid Id { get; set; }
    public Guid? FamilyId { get; set; }
    public Guid? ActorDeviceId { get; set; }
    public string Action { get; set; } = string.Empty;
    public string MetadataJson { get; set; } = "{}";
    public DateTimeOffset CreatedAtUtc { get; set; }
}
