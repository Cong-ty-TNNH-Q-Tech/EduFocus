using EduFocus.Ports.DTOs;

namespace EduFocus.Ports.Inbound;

/// <summary>
/// One supervisor decision for the resident watchdog. The host loops on
/// <see cref="TickAsync"/>; keeping a single tick testable means the restart
/// policy (heartbeat, hang kill, backoff, budget, clean exit) is verified
/// without starting real processes.
/// </summary>
public interface IWatchdogUseCase
{
    /// <param name="loginStart">
    /// True for the at-logon task so the app can also be started when protection
    /// is OFF and <c>StartWithWindows</c> is enabled.
    /// </param>
    Task<WatchdogTickResult> TickAsync(
        bool loginStart,
        CancellationToken cancellationToken = default);
}
