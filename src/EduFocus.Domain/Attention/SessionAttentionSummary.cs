using EduFocus.Domain.Entities;

namespace EduFocus.Domain.Attention;

/// <summary>
/// Attention and coverage view of a study session for reports (UC08, UC10).
/// Coverage compares monitored time with the time monitoring was expected, so
/// camera outages lower coverage instead of lowering attention. Monitored time
/// is always derived from the four attention counters and never includes
/// CameraUnavailable.
/// </summary>
public sealed record SessionAttentionSummary(
    int FocusedSeconds,
    int DistractedSeconds,
    int AbsentSeconds,
    int DrowsySeconds,
    int CameraUnavailableSeconds,
    int MonitoringEligibleSeconds,
    double? ConfidenceAvg)
{
    public const double LowCoverageThresholdPercent = 50;

    public const string LowCoverageWarning = "Dữ liệu chưa đủ tin cậy";

    public int MonitoredSeconds => FocusedSeconds + DistractedSeconds + AbsentSeconds + DrowsySeconds;

    /// <summary>Eligible time the camera could not observe; reports show it next to coverage.</summary>
    public int UnobservedSeconds => Math.Max(0, MonitoringEligibleSeconds - MonitoredSeconds);

    public double? AttentionPercent =>
        MonitoredSeconds > 0 ? FocusedSeconds * 100.0 / MonitoredSeconds : null;

    public double? CoveragePercent =>
        MonitoringEligibleSeconds > 0 ? MonitoredSeconds * 100.0 / MonitoringEligibleSeconds : null;

    /// <summary>True when attention percentages are based on too little usable time.</summary>
    public bool IsCoverageLow => CoveragePercent is < LowCoverageThresholdPercent;

    public string? Warning => IsCoverageLow ? LowCoverageWarning : null;

    public static SessionAttentionSummary From(StudySession session)
    {
        ArgumentNullException.ThrowIfNull(session);

        return new SessionAttentionSummary(
            session.FocusedSeconds,
            session.DistractedSeconds,
            session.AbsentSeconds,
            session.DrowsySeconds,
            session.CameraUnavailableSeconds,
            session.MonitoringEligibleSeconds,
            session.ConfidenceAvg);
    }
}
