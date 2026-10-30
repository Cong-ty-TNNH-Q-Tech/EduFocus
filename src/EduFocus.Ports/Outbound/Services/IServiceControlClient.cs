using EduFocus.Ports.ServiceControl;

namespace EduFocus.Ports.Outbound.Services;

/// <summary>
/// Read-only handshake with the protection service. Mutating commands flow
/// through <see cref="Inbound.IProtectionUseCase"/> implemented by the same
/// client so the UI cannot accidentally keep a second local engine.
/// </summary>
public interface IServiceControlClient
{
    Task<ProtectionServiceInfo> GetServiceInfoAsync(CancellationToken cancellationToken = default);
}
