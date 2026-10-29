using EduFocus.Ports.DTOs;

namespace EduFocus.Ports.Inbound;

/// <summary>
/// Protection ON/OFF state machine (UC03, UC04). Enabling and disabling are
/// manual parent operations and therefore require an authenticated Parent Mode
/// session. <see cref="ReconcileAsync"/> runs at startup as the system actor.
/// </summary>
public interface IProtectionUseCase
{
    Task<ProtectionStatus> GetStatusAsync(CancellationToken cancellationToken = default);

    Task<ProtectionOperationResult> EnableAsync(CancellationToken cancellationToken = default);

    Task<ProtectionOperationResult> DisableAsync(CancellationToken cancellationToken = default);

    Task<ProtectionOperationResult> ReconcileAsync(CancellationToken cancellationToken = default);
}
