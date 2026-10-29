using EduFocus.Domain.Attention;
using EduFocus.Domain.Entities;

namespace EduFocus.Domain.Reports;

/// <summary>
/// Day/week report for Parent Mode (UC10, issue #36). Session totals always
/// come from StudySessions so they survive sample retention; timeline, top
/// app/domain and attention events are detail data that may have expired.
/// </summary>
public sealed record StudyReport
{
    public const string ActivityDetailsExpiredWarning =
        "Dữ liệu chi tiết hoạt động đã hết thời hạn lưu trữ; tổng hợp phiên học vẫn được giữ.";

    public const string AttentionDetailsExpiredWarning =
        "Sự kiện tập trung đã hết thời hạn lưu trữ; tổng hợp phiên học vẫn được giữ.";

    public const string BlockedAttemptsNote =
        "Số lần bị chặn chỉ gồm những gì EduFocus quan sát được (best-effort).";

    public required ReportPeriod Period { get; init; }

    public IReadOnlyList<DailyStudySummary> Days { get; init; } = [];

    public IReadOnlyList<StudySession> Sessions { get; init; } = [];

    public IReadOnlyList<HourlyActivityBucket> Timeline { get; init; } = [];

    public IReadOnlyList<TopActivityItem> TopActivities { get; init; } = [];

    public IReadOnlyList<AttentionEvent> AttentionEvents { get; init; } = [];

    public int SessionCount { get; init; }

    public int ActiveSeconds { get; init; }

    public int StudySeconds { get; init; }

    public int DistractionSeconds { get; init; }

    public int NeutralSeconds { get; init; }

    public int OnlineClassSeconds { get; init; }

    public int BlockedAttemptCount { get; init; }

    public int FocusedSeconds { get; init; }

    public int DistractedSeconds { get; init; }

    public int AbsentSeconds { get; init; }

    public int DrowsySeconds { get; init; }

    public int CameraUnavailableSeconds { get; init; }

    public int MonitoringEligibleSeconds { get; init; }

    /// <summary>Duration-weighted across sessions; null until a session has confidence.</summary>
    public double? ConfidenceAvg { get; init; }

    public bool SessionsTruncated { get; init; }

    public bool TimelineTruncated { get; init; }

    public bool AttentionEventsTruncated { get; init; }

    /// <summary>
    /// A session counted foreground time but none of its samples remain, so
    /// the period totals cannot be drilled down for that session.
    /// </summary>
    public bool ActivityDetailsExpired { get; init; }

    /// <summary>
    /// A session counted monitored time but none of its attention events
    /// remain, so the period totals cannot be drilled down for that session.
    /// </summary>
    public bool AttentionDetailsExpired { get; init; }

    public bool HasSessions => SessionCount > 0;

    public bool HasWarnings => ActivityDetailsExpired || AttentionDetailsExpired;

    public SessionAttentionSummary Attention => new(
        FocusedSeconds,
        DistractedSeconds,
        AbsentSeconds,
        DrowsySeconds,
        CameraUnavailableSeconds,
        MonitoringEligibleSeconds,
        ConfidenceAvg);

    public IReadOnlyList<string> Warnings
    {
        get
        {
            if (!HasWarnings)
                return [];

            var warnings = new List<string>(2);

            if (ActivityDetailsExpired)
                warnings.Add(ActivityDetailsExpiredWarning);

            if (AttentionDetailsExpired)
                warnings.Add(AttentionDetailsExpiredWarning);

            return warnings;
        }
    }

    public string WarningText => Warnings.Count == 0 ? string.Empty : string.Join(" ", Warnings);

    public static StudyReport Empty(ReportPeriod period) => new()
    {
        Period = period,
        Days = period.EnumerateDays().Select(DailyStudySummary.Empty).ToArray()
    };
}
