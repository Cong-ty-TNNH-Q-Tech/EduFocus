using EduFocus.Domain.Attention;
using EduFocus.Domain.Entities;
using EduFocus.Domain.Enums;
using FluentAssertions;

namespace EduFocus.Domain.Tests.Attention;

public class SessionAttentionSummaryTests
{
    private static readonly DateTime Now = new(2026, 10, 30, 8, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void Summary_derives_monitored_percentages_from_the_counters()
    {
        var session = StudySession.Start(Now, new DateOnly(2026, 10, 30), SessionTrigger.Manual, true);
        session.AddMonitoringEligibleSeconds(200, Now.AddSeconds(1));
        session.AddAttentionSeconds(AttentionState.Focused, 80, Now.AddSeconds(2));
        session.AddAttentionSeconds(AttentionState.Distracted, 15, Now.AddSeconds(3));
        session.AddAttentionSeconds(AttentionState.Absent, 5, Now.AddSeconds(4));
        session.AddAttentionSeconds(AttentionState.Drowsy, 0, Now.AddSeconds(5));
        session.AddAttentionSeconds(AttentionState.CameraUnavailable, 40, Now.AddSeconds(6));
        session.SetAttentionConfidence(0.8, Now.AddSeconds(7));

        var summary = SessionAttentionSummary.From(session);

        summary.MonitoredSeconds.Should().Be(100);
        summary.MonitoredSeconds.Should().Be(
            summary.FocusedSeconds + summary.DistractedSeconds +
            summary.AbsentSeconds + summary.DrowsySeconds);
        summary.CameraUnavailableSeconds.Should().Be(40);
        summary.AttentionPercent.Should().BeApproximately(80, 0.0001);
        summary.CoveragePercent.Should().BeApproximately(50, 0.0001);
        summary.ConfidenceAvg.Should().BeApproximately(0.8, 0.0001);
        summary.IsCoverageLow.Should().BeFalse();
        summary.Warning.Should().BeNull();
    }

    [Fact]
    public void Coverage_below_fifty_percent_warns_that_data_is_unreliable()
    {
        var belowThreshold = Summary(monitoredSeconds: 49, eligibleSeconds: 100);
        var atThreshold = Summary(monitoredSeconds: 50, eligibleSeconds: 100);

        belowThreshold.IsCoverageLow.Should().BeTrue();
        belowThreshold.Warning.Should().Be(SessionAttentionSummary.LowCoverageWarning);
        atThreshold.IsCoverageLow.Should().BeFalse();
        atThreshold.Warning.Should().BeNull();
    }

    [Fact]
    public void Empty_counters_make_percentages_unknown_without_a_warning()
    {
        var summary = Summary(monitoredSeconds: 0, eligibleSeconds: 0);

        summary.MonitoredSeconds.Should().Be(0);
        summary.AttentionPercent.Should().BeNull();
        summary.CoveragePercent.Should().BeNull();
        summary.IsCoverageLow.Should().BeFalse();
        summary.Warning.Should().BeNull();
    }

    [Fact]
    public void Full_coverage_and_full_focus_are_reported_as_one_hundred()
    {
        var summary = Summary(monitoredSeconds: 60, eligibleSeconds: 60);

        summary.AttentionPercent.Should().Be(100);
        summary.CoveragePercent.Should().Be(100);
        summary.IsCoverageLow.Should().BeFalse();
    }

    [Fact]
    public void Camera_unavailable_time_lowers_coverage_but_not_attention()
    {
        var session = StudySession.Start(Now, new DateOnly(2026, 10, 30), SessionTrigger.Manual, true);
        session.AddMonitoringEligibleSeconds(100, Now.AddSeconds(1));
        session.AddAttentionSeconds(AttentionState.Focused, 40, Now.AddSeconds(2));
        session.AddAttentionSeconds(AttentionState.CameraUnavailable, 60, Now.AddSeconds(3));

        var summary = SessionAttentionSummary.From(session);

        summary.MonitoredSeconds.Should().Be(40);
        summary.CoveragePercent.Should().BeApproximately(40, 0.0001);
        summary.AttentionPercent.Should().Be(100);
        summary.IsCoverageLow.Should().BeTrue();
    }

    [Fact]
    public void Null_session_is_rejected()
    {
        var act = () => SessionAttentionSummary.From(null!);

        act.Should().Throw<ArgumentNullException>();
    }

    private static SessionAttentionSummary Summary(int monitoredSeconds, int eligibleSeconds)
    {
        var session = StudySession.Start(Now, new DateOnly(2026, 10, 30), SessionTrigger.Manual, true);

        if (monitoredSeconds > 0)
            session.AddAttentionSeconds(AttentionState.Focused, monitoredSeconds, Now.AddSeconds(1));

        if (eligibleSeconds > 0)
            session.AddMonitoringEligibleSeconds(eligibleSeconds, Now.AddSeconds(2));

        return SessionAttentionSummary.From(session);
    }
}
