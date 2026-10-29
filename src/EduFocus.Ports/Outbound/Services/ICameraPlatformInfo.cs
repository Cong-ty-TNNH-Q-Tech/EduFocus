namespace EduFocus.Ports.Outbound.Services;

/// <summary>
/// Platform capabilities that change camera remediation guidance. Reading the
/// OS version never requires administrator rights.
/// </summary>
public interface ICameraPlatformInfo
{
    /// <summary>Windows 11 22H2+ can share a camera between apps (Multi-App Camera).</summary>
    bool SupportsMultiAppCamera { get; }
}
