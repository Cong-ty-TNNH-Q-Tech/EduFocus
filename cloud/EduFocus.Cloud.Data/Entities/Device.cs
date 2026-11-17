namespace EduFocus.Cloud.Data.Entities;

public enum DeviceKind
{
    Desktop,
    Mobile,
}

public sealed class Device
{
    public Guid Id { get; set; }
    public Guid FamilyId { get; set; }
    public DeviceKind Kind { get; set; }
    public string DisplayName { get; set; } = string.Empty;
    public string Platform { get; set; } = string.Empty;
    public string? AppVersion { get; set; }
    public DateTimeOffset CreatedAtUtc { get; set; }
    public DateTimeOffset? LastSeenAtUtc { get; set; }
    public DateTimeOffset? RevokedAtUtc { get; set; }
}
