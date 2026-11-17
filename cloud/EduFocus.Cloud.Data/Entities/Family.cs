namespace EduFocus.Cloud.Data.Entities;

public sealed class Family
{
    public Guid Id { get; set; }
    public DateTimeOffset CreatedAtUtc { get; set; }
    public string TimeZoneId { get; set; } = "Asia/Ho_Chi_Minh";
    public DateTimeOffset? DeletedAtUtc { get; set; }
}
