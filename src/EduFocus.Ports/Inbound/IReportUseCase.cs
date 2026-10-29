using EduFocus.Domain.Reports;

namespace EduFocus.Ports.Inbound;

/// <summary>
/// Day/week reports for Parent Mode (UC10, issue #36). Session totals come from
/// StudySessions; timeline, top app/domain and attention events are detail data
/// limited to the currently retained samples.
/// </summary>
public interface IReportUseCase
{
    /// <summary>
    /// Builds a report for the inclusive local-date period. A period without
    /// sessions returns an empty report with one zeroed row per day.
    /// </summary>
    Task<StudyReport> GetAsync(ReportPeriod period, CancellationToken cancellationToken = default);
}
