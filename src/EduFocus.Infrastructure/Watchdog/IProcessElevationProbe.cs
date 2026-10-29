namespace EduFocus.Infrastructure.Watchdog;

/// <summary>Elevation check seam so scheduled-task registration can be verified in tests.</summary>
public interface IProcessElevationProbe
{
    bool IsElevated { get; }
}

public sealed class WindowsProcessElevationProbe : IProcessElevationProbe
{
    public bool IsElevated => Environment.IsPrivilegedProcess;
}
