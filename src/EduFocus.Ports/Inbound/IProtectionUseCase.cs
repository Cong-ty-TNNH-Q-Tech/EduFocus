using EduFocus.Ports.DTOs;

namespace EduFocus.Ports.Inbound;

/// <summary>
/// Protection ON/OFF state machine (UC03, UC04). Enabling and disabling are
/// manual parent operations and therefore require an authenticated Parent Mode
/// session. <see cref="ReconcileAsync"/> runs at startup as the system actor and
/// the schedule methods are called only by the scheduler; neither requires a PIN.
/// </summary>
public interface IProtectionUseCase
{
    Task<ProtectionStatus> GetStatusAsync(CancellationToken cancellationToken = default);

    Task<ProtectionOperationResult> EnableAsync(CancellationToken cancellationToken = default);

    Task<ProtectionOperationResult> DisableAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Parent-authorized exit path (UC12): turns protection off without creating a
    /// schedule override so the clean exit is not re-enforced on the next start.
    /// </summary>
    Task<ProtectionOperationResult> DisableForExitAsync(CancellationToken cancellationToken = default);

    Task<ProtectionOperationResult> ReconcileAsync(CancellationToken cancellationToken = default);

    /// <summary>System actor: turns protection on for an active schedule window.</summary>
    Task<ProtectionOperationResult> EnableForScheduleAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// System actor: turns protection off only when the schedule owns the current
    /// ON state, so manually enabled protection survives the end of a window.
    /// </summary>
    Task<ProtectionOperationResult> DisableForScheduleAsync(CancellationToken cancellationToken = default);
}
