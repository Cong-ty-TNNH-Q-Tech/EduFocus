namespace EduFocus.UseCases.Sessions;

/// <summary>
/// Heartbeat cadence (UC07: every 10 seconds). The delta cap keeps a suspended
/// or stalled process from adding downtime to ActiveSeconds; detailed sleep
/// handling is layered on by the power-event work in a later phase.
/// </summary>
public sealed class StudySessionHeartbeatOptions
{
    public static readonly TimeSpan DefaultInterval = TimeSpan.FromSeconds(10);
    public static readonly TimeSpan DefaultMaxActiveDelta = TimeSpan.FromSeconds(60);

    public TimeSpan Interval { get; init; } = DefaultInterval;

    public TimeSpan MaxActiveDelta { get; init; } = DefaultMaxActiveDelta;
}
