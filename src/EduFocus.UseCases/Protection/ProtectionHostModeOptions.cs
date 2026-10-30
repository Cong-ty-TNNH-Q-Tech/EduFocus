namespace EduFocus.UseCases.Protection;

/// <summary>
/// Tells shared use cases whether the installed protection service owns the
/// engine, heartbeat and study sessions. In service mode the UI only owns
/// camera/activity monitoring, so it must not write session lifecycle rows or
/// stop the service heartbeat on Windows shutdown.
/// </summary>
public sealed class ProtectionHostModeOptions
{
    public bool ServiceManaged { get; init; }
}
