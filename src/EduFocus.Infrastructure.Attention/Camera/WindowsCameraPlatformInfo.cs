using EduFocus.Ports.Outbound.Services;

namespace EduFocus.Infrastructure.Attention.Camera;

/// <summary>
/// Windows build probe for the Multi-App Camera feature. Windows 11 22H2
/// (build 22621) introduced camera sharing between applications; older builds
/// can only expose the device to one app at a time.
/// </summary>
public sealed class WindowsCameraPlatformInfo : ICameraPlatformInfo
{
    private const int MultiAppCameraMinimumBuild = 22621;

    public bool SupportsMultiAppCamera =>
        OperatingSystem.IsWindowsVersionAtLeast(10, 0, MultiAppCameraMinimumBuild);
}
