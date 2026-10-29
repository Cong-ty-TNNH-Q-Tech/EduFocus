namespace EduFocus.UseCases.Activity;

/// <summary>
/// Foreground sampling cadence (UC07). Sampling is independent from the study
/// session heartbeat; the defaults write at most one sample every 15 seconds
/// and require a foreground context to stay stable for 2 seconds.
/// </summary>
public sealed record ActivityTrackingOptions
{
    public TimeSpan PollInterval { get; init; } = TimeSpan.FromSeconds(1);

    public TimeSpan SampleInterval { get; init; } = TimeSpan.FromSeconds(15);

    public TimeSpan ForegroundDebounce { get; init; } = TimeSpan.FromSeconds(2);

    public TimeSpan IdleThreshold { get; init; } = TimeSpan.FromMinutes(5);

    /// <summary>Largest monotonic gap counted into one sample, guarding resume.</summary>
    public TimeSpan MaxSampleGap { get; init; } = TimeSpan.FromSeconds(60);
}
