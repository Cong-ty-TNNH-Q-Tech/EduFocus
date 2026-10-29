using EduFocus.Ports.DTOs;

namespace EduFocus.Ports.Outbound.Services;

/// <summary>
/// Windows process seam used by the watchdog. Implementations must verify the
/// process start time before killing so a recycled PID is never terminated.
/// </summary>
public interface IWatchdogProcessPort
{
    /// <summary>True when another process holds the app's single-instance mutex.</summary>
    bool IsAppInstanceRunning();

    bool IsProcessAlive(long processId, DateTime processStartedAtUtc);

    /// <summary>Returns false when the process is gone, reused, or cannot be killed.</summary>
    bool TryKill(long processId, DateTime processStartedAtUtc);

    /// <summary>Starts the normal app window; null when the executable is unavailable.</summary>
    WatchdogProcessInfo? StartApp();
}
