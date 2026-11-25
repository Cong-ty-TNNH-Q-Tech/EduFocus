namespace EduFocus.Cloud.Data.Entities;

public sealed class PushRegistration
{
    public Guid Id { get; set; }
    public Guid DeviceId { get; set; }
    public string Endpoint { get; set; } = string.Empty;
    public string P256dh { get; set; } = string.Empty;
    public string Auth { get; set; } = string.Empty;
    public string Platform { get; set; } = string.Empty;
    public DateTimeOffset UpdatedAtUtc { get; set; }
    public DateTimeOffset? InvalidatedAtUtc { get; set; }
}
