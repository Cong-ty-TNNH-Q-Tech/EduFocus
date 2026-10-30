namespace EduFocus.WPF.Services;

/// <summary>
/// Shared flag for the single approved exit path. The main window close guard
/// hides to the tray until a PIN exit (UC12) has approved the shutdown.
/// </summary>
public sealed class AppExitState
{
    public bool IsExitApproved { get; private set; }

    public void ApproveExit() => IsExitApproved = true;
}
