using EduFocus.Domain.Reports;
using EduFocus.Ports.DTOs;

namespace EduFocus.Ports.Inbound;

/// <summary>
/// Exports the report the parent is currently viewing (UC10/UC11, issue #37).
/// The supplied snapshot supplies the summary totals, so the CSV always agrees
/// with the on-screen report; activity samples are read through the
/// privacy-safe report projection and never expose titles or URLs.
/// </summary>
public interface IReportExportUseCase
{
    Task<ReportExportResult> ExportCsvAsync(
        StudyReport report,
        CancellationToken cancellationToken = default);
}
