namespace EduFocus.Infrastructure.Watchdog;

/// <summary>
/// Runtime folder shared by the app and the watchdog. It lives under
/// <c>%ProgramData%\EduFocus\runtime</c> so the elevated watchdog can read it
/// even when a standard user is signed in; tests point it at a temp folder.
/// </summary>
public sealed class WatchdogRuntimeOptions
{
    public const string DefaultFolderName = "EduFocus";
    public const string RuntimeFolderName = "runtime";
    public const string ExecutableName = "EduFocus.exe";

    public static string DefaultDirectory =>
        Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
            DefaultFolderName,
            RuntimeFolderName);

    public string RuntimeDirectory { get; init; } = DefaultDirectory;

    /// <summary>Overrides the executable used for restarts; defaults to the running EduFocus.exe.</summary>
    public string? ExecutablePath { get; init; }

    public string? ResolveExecutablePath()
    {
        if (!string.IsNullOrWhiteSpace(ExecutablePath))
            return File.Exists(ExecutablePath) ? Path.GetFullPath(ExecutablePath) : null;

        var processPath = Environment.ProcessPath;
        if (!string.IsNullOrWhiteSpace(processPath) &&
            string.Equals(Path.GetFileName(processPath), ExecutableName, StringComparison.OrdinalIgnoreCase))
        {
            return processPath;
        }

        var besideTheApp = Path.Combine(AppContext.BaseDirectory, ExecutableName);
        return File.Exists(besideTheApp) ? besideTheApp : null;
    }
}
