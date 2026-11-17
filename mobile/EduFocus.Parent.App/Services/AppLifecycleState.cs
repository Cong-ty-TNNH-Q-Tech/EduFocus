namespace EduFocus.Parent.App.Services;

/// <summary>
/// Tracks whether a window is visible so push messages can become an in-app
/// toast while foregrounded and a system notification otherwise.
/// </summary>
public static class AppLifecycleState
{
    public static bool IsForeground { get; set; }
}
