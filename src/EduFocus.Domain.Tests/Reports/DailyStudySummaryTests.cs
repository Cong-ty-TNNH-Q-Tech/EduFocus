using EduFocus.Domain.Attention;
using EduFocus.Domain.Reports;
using FluentAssertions;

namespace EduFocus.Domain.Tests.Reports;

public class DailyStudySummaryTests
{
    private static readonly DateOnly Today = new(2026, 10, 30);

    [Fact]
    public void Empty_day_has_no_sessions_and_unknown_percentages()
    {
        var summary = DailyStudySummary.Empty(Today);

        summary.LocalDate.Should().Be(Today);
        summary.HasSessions.Should().BeFalse();
        summary.HasActiveSession.Should().BeFalse();
        summary.SessionCount.Should().Be(0);
        summary.Attention.MonitoredSeconds.Should().Be(0);
        summary.Attention.AttentionPercent.Should().BeNull();
        summary.Attention.CoveragePercent.Should().BeNull();
        summary.Attention.Warning.Should().BeNull();
    }

    [Fact]
    public void Summed_counters_produce_the_same_attention_and_coverage_rules_as_a_session()
    {
        var summary = new DailyStudySummary
        {
            LocalDate = Today,
            SessionCount = 3,
            ActiveSeconds = 5_400,
            StudySeconds = 4_800,
            DistractionSeconds = 600,
            BlockedAttemptCount = 4,
            FocusedSeconds = 1_800,
            DistractedSeconds = 300,
            AbsentSeconds = 200,
            DrowsySeconds = 100,
            CameraUnavailableSeconds = 600,
            MonitoringEligibleSeconds = 3_000
        };

        summary.HasSessions.Should().BeTrue();
        summary.HasActiveSession.Should().BeFalse();
        summary.Attention.MonitoredSeconds.Should().Be(2_400);
        summary.Attention.AttentionPercent.Should().BeApproximately(75, 0.0001);
        summary.Attention.CoveragePercent.Should().BeApproximately(80, 0.0001);
        summary.Attention.CameraUnavailableSeconds.Should().Be(600);
        summary.Attention.UnobservedSeconds.Should().Be(600);
        summary.Attention.IsCoverageLow.Should().BeFalse();
        summary.Attention.ConfidenceAvg.Should().BeNull();
    }

    [Fact]
    public void Coverage_below_fifty_percent_carries_the_warning_to_the_dashboard()
    {
        var summary = new DailyStudySummary
        {
            LocalDate = Today,
            SessionCount = 1,
            FocusedSeconds = 200,
            DistractedSeconds = 100,
            MonitoringEligibleSeconds = 1_000
        };

        summary.Attention.CoveragePercent.Should().BeApproximately(30, 0.0001);
        summary.Attention.IsCoverageLow.Should().BeTrue();
        summary.Attention.Warning.Should().Be(SessionAttentionSummary.LowCoverageWarning);
        summary.Attention.UnobservedSeconds.Should().Be(700);
    }

    [Fact]
    public void Active_session_is_reported_with_its_start_time()
    {
        var startedAt = new DateTime(2026, 10, 30, 8, 15, 0, DateTimeKind.Utc);
        var summary = new DailyStudySummary
        {
            LocalDate = Today,
            SessionCount = 1,
            ActiveSessionStartedAtUtc = startedAt
        };

        summary.HasActiveSession.Should().BeTrue();
        summary.ActiveSessionStartedAtUtc.Should().Be(startedAt);
    }

    [Fact]
    public void Camera_unavailable_time_is_outside_the_attention_denominator()
    {
        var summary = new DailyStudySummary
        {
            LocalDate = Today,
            SessionCount = 2,
            FocusedSeconds = 300,
            CameraUnavailableSeconds = 900,
            MonitoringEligibleSeconds = 1_200
        };

        summary.Attention.MonitoredSeconds.Should().Be(300);
        summary.Attention.AttentionPercent.Should().Be(100);
        summary.Attention.CoveragePercent.Should().Be(25);
        summary.Attention.IsCoverageLow.Should().BeTrue();
    }
}
