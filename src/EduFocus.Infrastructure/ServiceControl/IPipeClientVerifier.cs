using System.IO.Pipes;

namespace EduFocus.Infrastructure.ServiceControl;

/// <summary>
/// Checks the process at the other end of a connected control pipe. The pipe
/// ACL already excludes standard users; this second check keeps an arbitrary
/// elevated process from issuing protection commands through the service.
/// </summary>
public interface IPipeClientVerifier
{
    bool IsAllowed(NamedPipeServerStream pipe, out string reason);
}
