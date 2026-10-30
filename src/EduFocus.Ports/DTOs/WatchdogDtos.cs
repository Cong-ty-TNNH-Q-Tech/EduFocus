namespace EduFocus.Ports.DTOs;

/// <summary>Why the watchdog (re)started EduFocus. Never contains user data.</summary>
public enum WatchdogRestartReason
{
    /// <summary>The app was not running at login or after a Windows shutdown.</summary>
    Recovery,

    /// <summary>The app process disappeared while protection was still ON.</summary>
    UnexpectedExit,

    /// <summary>The app process stayed alive but stopped writing heartbeats.</summary>
    Hang
}

public enum WatchdogTickAction
{
    Wait,
    Stop,
    StartedApp,
    StartedAppAtLogin,
    KilledHang,
    GaveUp
}

public enum WatchdogRegistrationMode
{
    None,
    LogonOnly,
    LogonAndHealth
}

/// <summary>
/// Written by the running app every few seconds. The PID plus process start
/// time identify one instance so a recycled PID is never mistaken for the app.
/// </summary>
public sealed record WatchdogHeartbeat(
    long ProcessId,
    DateTime ProcessStartedAtUtc,
    DateTime UpdatedAtUtc);

public sealed record WatchdogProcessInfo(long ProcessId, DateTime StartedAtUtc);

/// <summary>
/// Persisted restart budget and pending restart. Written only by the watchdog so
/// a crash of the watchdog itself cannot reset the budget.
/// </summary>
public sealed record WatchdogRestartState
{
    public int RestartCount { get; init; }

    public DateTime? WindowStartedAtUtc { get; init; }

    public DateTime? LastRestartAtUtc { get; init; }

    public DateTime? NextRestartAtUtc { get; init; }

    public DateTime? GaveUpUntilUtc { get; init; }

    public DateTime? GaveUpRecordedAtUtc { get; init; }

    public long? PendingProcessId { get; init; }

    public DateTime? PendingProcessStartedAtUtc { get; init; }

    public WatchdogRestartReason? PendingReason { get; init; }
}

public sealed record WatchdogTickResult(
    WatchdogTickAction Action,
    TimeSpan Delay,
    WatchdogRestartReason? Reason = null,
    long? ProcessId = null)
{
    public static WatchdogTickResult Wait(TimeSpan delay) =>
        new(WatchdogTickAction.Wait, delay);

    public static WatchdogTickResult Stop(TimeSpan delay) =>
        new(WatchdogTickAction.Stop, delay);
}
