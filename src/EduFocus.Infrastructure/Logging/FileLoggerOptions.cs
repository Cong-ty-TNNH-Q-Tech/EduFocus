namespace EduFocus.Infrastructure.Logging;

/// <summary>
/// File logger settings. Logs live under the current user's local application
/// data so writing never requires administrator rights; the default keeps 14
/// days of files capped at 10 MB each (system architecture §12).
/// </summary>
public sealed record FileLoggerOptions
{
    public const int DefaultRetentionDays = 14;
    public const long DefaultFileSizeLimitBytes = 10 * 1024 * 1024;

    public static string DefaultDirectory =>
        Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "EduFocus",
            "logs");

    public string DirectoryPath { get; init; } = DefaultDirectory;

    public int RetentionDays { get; init; } = DefaultRetentionDays;

    public long FileSizeLimitBytes { get; init; } = DefaultFileSizeLimitBytes;
}
