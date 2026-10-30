namespace EduFocus.UseCases.Attention;

/// <summary>
/// Attention smoothing and accounting settings. The window is deliberately
/// short (5–10 seconds) so it removes single-frame noise without hiding a real
/// state change; MaxSampleGap prevents suspend/resume gaps from being counted.
/// </summary>
public sealed record AttentionMonitoringOptions(
    TimeSpan WindowDuration,
    int MinimumWindowSamples,
    double MajorityShare,
    TimeSpan MaxSampleGap)
{
    public static AttentionMonitoringOptions Default { get; } = new(
        TimeSpan.FromSeconds(6),
        MinimumWindowSamples: 3,
        MajorityShare: 0.6,
        MaxSampleGap: TimeSpan.FromSeconds(5));
}
