namespace EduFocus.Cloud.Api.Options;

public sealed class RateLimitOptions
{
    public const string SectionName = "RateLimiting";

    public int PairingCreatePerHour { get; set; } = 10;

    public int PairingClaimPerHour { get; set; } = 20;

    public int RequestsPerMinute { get; set; } = 120;

    public int SyncRequestsPerMinute { get; set; } = 60;
}
