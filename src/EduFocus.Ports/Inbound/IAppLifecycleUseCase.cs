using EduFocus.Ports.DTOs;

namespace EduFocus.Ports.Inbound;

/// <summary>
/// Application exit and Windows session-ending flows (UC12). A PIN exit is the
/// only path that clears protection and records a clean shutdown; a Windows
/// shutdown/logoff keeps the persisted protection state for recovery at the
/// next sign-in and must never block the operating system.
/// </summary>
public interface IAppLifecycleUseCase
{
    /// <summary>
    /// Verifies the parent PIN, then disables protection without a schedule
    /// override, completes the active session and records the clean exit.
    /// The Parent Mode session is always locked afterwards.
    /// </summary>
    Task<AppExitResult> ExitWithPinAsync(string pin, CancellationToken cancellationToken = default);

    /// <summary>
    /// Best-effort Windows shutdown/logoff record: interrupts the active session
    /// with the <c>SystemShutdown</c> note, keeps protection ON and never throws,
    /// so the OS shutdown is never blocked.
    /// </summary>
    Task NotifySystemShutdownAsync(CancellationToken cancellationToken = default);
}
