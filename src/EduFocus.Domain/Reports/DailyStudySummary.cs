using EduFocus.Domain.Attention;

namespace EduFocus.Domain.Reports;

/// <summary>
/// Aggregated study totals for one local day (database schema §13.1, issue #35).
/// The dashboard reads one row per session, never raw samples, and derives the
/// same attention/coverage rules as a single session so a camera outage always
/// lowers coverage instead of attention.
/// </summary>
public sealed record DailyStudySummary
{
    public required DateOnly LocalDate { get; init; }

    public int SessionCount { get; init; }

    public int ActiveSeconds { get; init; }

    public int StudySeconds { get; init; }

    public int DistractionSeconds { get; init; }

    public int NeutralSeconds { get; init; }

    public int OnlineClassSeconds { get; init; }

    public int BlockedAttemptCount { get; init; }

    public DateTime? ActiveSessionStartedAtUtc { get; init; }

    public int FocusedSeconds { get; init; }

    public int DistractedSeconds { get; init; }

    public int AbsentSeconds { get; init; }

    public int DrowsySeconds { get; init; }

    public int CameraUnavailableSeconds { get; init; }

    public int MonitoringEligibleSeconds { get; init; }

    public static DailyStudySummary Empty(DateOnly localDate) => new() { LocalDate = localDate };

    public bool HasSessions => SessionCount > 0;

    public bool HasActiveSession => ActiveSessionStartedAtUtc.HasValue;

    /// <summary>
    /// Day-level attention view. Confidence is intentionally null: the stored
    /// per-session averages cannot be combined without their weights, and the
    /// dashboard does not display confidence.
    /// </summary>
    public SessionAttentionSummary Attention => new(
        FocusedSeconds,
        DistractedSeconds,
        AbsentSeconds,
        DrowsySeconds,
        CameraUnavailableSeconds,
        MonitoringEligibleSeconds,
        ConfidenceAvg: null);
}
