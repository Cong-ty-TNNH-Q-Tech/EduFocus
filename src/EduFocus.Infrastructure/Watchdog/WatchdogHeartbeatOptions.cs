namespace EduFocus.Infrastructure.Watchdog;

/// <summary>Cadence of the app liveness beacon. It must be well below the watchdog stale threshold.</summary>
public sealed record WatchdogHeartbeatOptions
{
    public TimeSpan Interval { get; init; } = TimeSpan.FromSeconds(5);
}
