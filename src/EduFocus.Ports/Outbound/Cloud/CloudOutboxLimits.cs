namespace EduFocus.Ports.Outbound.Cloud;

/// <summary>Queue bounds and batching rules for the cloud outbox (#87).</summary>
public static class CloudOutboxLimits
{
    public const int MaxPendingEvents = 5_000;
    public const int BatchSize = 50;

    public static readonly TimeSpan MaxEventAge = TimeSpan.FromDays(7);
}
