namespace EduFocus.Ports.DTOs;

/// <summary>
/// Result of a report CSV export (UC10/UC11, issue #37). The file only contains
/// report-safe fields; the path is surfaced to the parent together with a
/// reminder that the file holds sensitive study data.
/// </summary>
public sealed record ReportExportResult(
    string FileName,
    string FilePath,
    int DayRowCount,
    int SessionRowCount,
    int ActivitySampleRowCount,
    int AttentionEventRowCount,
    IReadOnlyList<string> Warnings)
{
    public int DataRowCount =>
        1 + DayRowCount + SessionRowCount + ActivitySampleRowCount + AttentionEventRowCount;
}
