namespace EduFocus.Cloud.Data.Entities;

public sealed class DeviceToken
{
    public Guid Id { get; set; }
    public Guid DeviceId { get; set; }
    public string TokenHash { get; set; } = string.Empty;
    public string Scopes { get; set; } = string.Empty;
    public DateTimeOffset CreatedAtUtc { get; set; }
    public DateTimeOffset? LastUsedAtUtc { get; set; }
    public DateTimeOffset? RevokedAtUtc { get; set; }
}
