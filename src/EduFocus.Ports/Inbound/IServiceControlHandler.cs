using EduFocus.Ports.ServiceControl;

namespace EduFocus.Ports.Inbound;

/// <summary>
/// Service-side command boundary. The named-pipe host authenticates the calling
/// process before invoking this handler; the handler only applies the command
/// through the same use cases the local app used.
/// </summary>
public interface IServiceControlHandler
{
    Task<ServiceControlResponse> HandleAsync(
        ServiceControlRequest request,
        CancellationToken cancellationToken = default);
}
