using EduFocus.Domain.Reports;

namespace EduFocus.Ports.Inbound;

/// <summary>
/// Today dashboard for Parent Mode and Student Mode (UC10, issue #35). The
/// summary is aggregated from session rows by the local start date, so it can
/// be polled without scanning raw samples.
/// </summary>
public interface IDashboardUseCase
{
    /// <summary>
    /// Returns the aggregate for the current local day. A day without sessions
    /// returns <see cref="DailyStudySummary.Empty"/>; the database is never
    /// asked to invent rows.
    /// </summary>
    Task<DailyStudySummary> GetTodayAsync(CancellationToken cancellationToken = default);
}
