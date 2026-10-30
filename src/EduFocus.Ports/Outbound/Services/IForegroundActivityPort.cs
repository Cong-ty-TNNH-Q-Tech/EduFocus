namespace EduFocus.Ports.Outbound.Services;

/// <summary>
/// Raw foreground-window metadata. The window title is captured as-is and must
/// be sanitized before persistence; implementations never include document
/// content, clipboard data or page text.
/// </summary>
public sealed record ForegroundActivitySnapshot(
    int ProcessId,
    nint WindowHandle,
    string? ProcessName,
    string? AppName,
    string? WindowTitle,
    int IdleSeconds);

/// <summary>
/// Reads the current foreground window and idle time (UC07, issue #26).
/// Returns null when no foreground window is available; implementations must
/// not throw for access-denied or a process that disappeared.
/// </summary>
public interface IForegroundActivityPort
{
    ForegroundActivitySnapshot? GetForegroundActivity();
}
