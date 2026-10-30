using EduFocus.Domain.Reports;
using EduFocus.Ports.Inbound;
using EduFocus.Ports.Outbound.Persistence;

namespace EduFocus.UseCases.Reports;

/// <summary>
/// Resolves the local calendar day and delegates the aggregate to the session
/// repository (UC10, issue #35). Reporting stays read-only; polled renders
/// reuse one aggregate query instead of scanning activity samples.
/// </summary>
public sealed class DashboardService : IDashboardUseCase
{
    private readonly IStudySessionRepository _sessions;
    private readonly TimeProvider _timeProvider;

    public DashboardService(IStudySessionRepository sessions, TimeProvider timeProvider)
    {
        _sessions = sessions ?? throw new ArgumentNullException(nameof(sessions));
        _timeProvider = timeProvider ?? throw new ArgumentNullException(nameof(timeProvider));
    }

    public async Task<DailyStudySummary> GetTodayAsync(CancellationToken cancellationToken = default)
    {
        var today = DateOnly.FromDateTime(_timeProvider.GetLocalNow().DateTime);

        return await _sessions.GetDailySummaryAsync(today, cancellationToken).ConfigureAwait(false)
            ?? DailyStudySummary.Empty(today);
    }
}
