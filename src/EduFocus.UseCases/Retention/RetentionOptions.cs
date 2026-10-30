namespace EduFocus.UseCases.Retention;

/// <summary>
/// Retention cadence (UC11): batch size keeps each delete short, audit rows use
/// a fixed one-year window because AppSettings exposes no audit retention knob.
/// </summary>
public sealed class RetentionOptions
{
    public const int DefaultBatchSize = 500;
    public const int DefaultAuditRetentionDays = 365;

    public int BatchSize { get; init; } = DefaultBatchSize;

    public int AuditRetentionDays { get; init; } = DefaultAuditRetentionDays;
}
