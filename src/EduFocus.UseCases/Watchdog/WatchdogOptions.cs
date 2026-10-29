namespace EduFocus.UseCases.Watchdog;

/// <summary>
/// Restart policy. The defaults restart a killed app within one poll (≤ 30 s
/// target) while a repeated crash is rate-limited to five starts per ten
/// minutes and slowed by exponential backoff.
/// </summary>
public sealed class WatchdogOptions
{
    public TimeSpan PollInterval { get; init; } = TimeSpan.FromSeconds(5);

    public TimeSpan HeartbeatStaleAfter { get; init; } = TimeSpan.FromSeconds(30);

    /// <summary>Grace after the watchdog starts before a stale beat means "hang".</summary>
    public TimeSpan StartupGrace { get; init; } = TimeSpan.FromSeconds(30);

    /// <summary>A larger gap between ticks means the machine slept; skip hang detection once.</summary>
    public TimeSpan ResumeGapThreshold { get; init; } = TimeSpan.FromSeconds(15);

    public TimeSpan RestartWindow { get; init; } = TimeSpan.FromMinutes(10);

    public TimeSpan StableResetAfter { get; init; } = TimeSpan.FromMinutes(10);

    public int MaxRestartsPerWindow { get; init; } = 5;

    public TimeSpan FirstRestartDelay { get; init; } = TimeSpan.FromSeconds(5);

    public TimeSpan RestartBackoffStep { get; init; } = TimeSpan.FromSeconds(5);

    public TimeSpan MaxRestartDelay { get; init; } = TimeSpan.FromSeconds(30);

    /// <summary>Wait after killing a hung app so its mutex and files are released.</summary>
    public TimeSpan HangKillDelay { get; init; } = TimeSpan.FromSeconds(1);

    public TimeSpan StopWait { get; init; } = TimeSpan.FromSeconds(5);
}
