namespace EduFocus.Ports.Outbound.Services;

/// <summary>
/// Writes a CSV export for Parent Mode (UC11, issue #37). Implementations use
/// UTF-8 with BOM so Excel renders Vietnamese correctly, refuse path
/// separators in the file name, and never overwrite an existing export.
/// </summary>
public interface ICsvExportStore
{
    /// <summary>
    /// Saves the content as a UTF-8 CSV with BOM and returns the full path. A
    /// numeric suffix is added when the requested name already exists.
    /// </summary>
    Task<string> SaveAsync(
        string fileName,
        string content,
        CancellationToken cancellationToken = default);
}
