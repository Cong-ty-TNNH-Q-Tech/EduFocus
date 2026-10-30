namespace EduFocus.Infrastructure.Protection;

/// <summary>
/// Hosts file and backup locations for the website blocker. Defaults follow
/// system architecture §6.2: the Windows hosts file and a backup/journal folder
/// under <c>%ProgramData%\EduFocus</c>. Tests point both paths at a temporary
/// directory so no elevation is required.
/// </summary>
public sealed record HostsFileOptions
{
    public const string DefaultBackupFolderName = "hosts-backup";

    public static string DefaultHostsPath =>
        Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.System),
            "drivers",
            "etc",
            "hosts");

    public static string DefaultBackupDirectory =>
        Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
            "EduFocus",
            DefaultBackupFolderName);

    public string HostsPath { get; init; } = DefaultHostsPath;

    public string BackupDirectory { get; init; } = DefaultBackupDirectory;

    public int WriteAttempts { get; init; } = 3;

    public TimeSpan WriteRetryDelay { get; init; } = TimeSpan.FromMilliseconds(200);
}
