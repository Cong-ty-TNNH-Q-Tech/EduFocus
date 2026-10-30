using System.IO.Pipes;

namespace EduFocus.Infrastructure.ServiceControl;

/// <summary>
/// Accepts only the EduFocus UI executable installed next to the service.
/// Standard users cannot open the pipe at all; this blocks an elevated process
/// using a different image from impersonating the UI.
/// </summary>
public sealed class ExpectedProcessPipeClientVerifier : IPipeClientVerifier
{
    private readonly string _expectedImagePath;

    public ExpectedProcessPipeClientVerifier(string expectedImagePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(expectedImagePath);
        _expectedImagePath = Path.GetFullPath(expectedImagePath);
    }

    public bool IsAllowed(NamedPipeServerStream pipe, out string reason)
    {
        ArgumentNullException.ThrowIfNull(pipe);

        if (!OperatingSystem.IsWindows())
        {
            reason = "named pipe identity checks require Windows";
            return false;
        }

        var processId = WindowsProcessIdentity.GetClientProcessId(pipe);
        if (processId is null)
        {
            reason = "client process id is unavailable";
            return false;
        }

        var imagePath = WindowsProcessIdentity.TryGetImagePath(processId.Value);
        if (imagePath is null)
        {
            reason = "client image path is unavailable";
            return false;
        }

        if (!WindowsProcessIdentity.PathsEqual(imagePath, _expectedImagePath))
        {
            reason = "client image path does not match EduFocus";
            return false;
        }

        reason = string.Empty;
        return true;
    }
}
