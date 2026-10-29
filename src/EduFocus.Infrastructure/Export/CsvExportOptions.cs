namespace EduFocus.Infrastructure.Export;

/// <summary>
/// CSV export settings (UC11, issue #37). Exports live under the current
/// user's local application data, next to technical logs, so writing never
/// requires administrator rights. The path is shown to the parent after every
/// export.
/// </summary>
public sealed record CsvExportOptions
{
    public static string DefaultDirectory =>
        Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "EduFocus",
            "exports");

    public string DirectoryPath { get; init; } = DefaultDirectory;
}
