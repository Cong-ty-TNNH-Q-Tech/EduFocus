namespace EduFocus.Cloud.Data.Entities;

public sealed class PairingSession
{
    public Guid Id { get; set; }
    public Guid FamilyId { get; set; }
    public Guid DesktopDeviceId { get; set; }
    public string CodeHash { get; set; } = string.Empty;
    public DateTimeOffset CreatedAtUtc { get; set; }
    public DateTimeOffset ExpiresAtUtc { get; set; }
    public DateTimeOffset? ClaimedAtUtc { get; set; }
    public Guid? ClaimedByDeviceId { get; set; }
    public int AttemptCount { get; set; }
}
