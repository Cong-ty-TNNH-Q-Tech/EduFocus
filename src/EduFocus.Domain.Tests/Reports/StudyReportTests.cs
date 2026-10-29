using EduFocus.Domain.Reports;
using FluentAssertions;

namespace EduFocus.Domain.Tests.Reports;

public class StudyReportTests
{
    private static readonly ReportPeriod Week = ReportPeriod.Week(new DateOnly(2026, 10, 30));

    [Fact]
    public void Empty_report_keeps_one_zeroed_row_per_day()
    {
        var report = StudyReport.Empty(Week);

        report.Period.Should().Be(Week);
        report.HasSessions.Should().BeFalse();
        report.SessionCount.Should().Be(0);
        report.Days.Should().HaveCount(7);
        report.Days.Should().OnlyContain(day => day.SessionCount == 0);
        report.Days.Select(day => day.LocalDate).Should().Equal(Week.EnumerateDays());
        report.Attention.AttentionPercent.Should().BeNull();
        report.Attention.CoveragePercent.Should().BeNull();
        report.HasWarnings.Should().BeFalse();
        report.WarningText.Should().BeEmpty();
    }

    [Fact]
    public void Totals_reuse_the_session_attention_and_coverage_rules()
    {
        var report = StudyReport.Empty(Week) with
        {
            SessionCount = 2,
            StudySeconds = 3_600,
            FocusedSeconds = 1_500,
            DistractedSeconds = 300,
            AbsentSeconds = 100,
            DrowsySeconds = 100,
            CameraUnavailableSeconds = 1_000,
            MonitoringEligibleSeconds = 4_000,
            ConfidenceAvg = 0.8
        };

        report.HasSessions.Should().BeTrue();
        report.Attention.MonitoredSeconds.Should().Be(2_000);
        report.Attention.AttentionPercent.Should().BeApproximately(75, 0.0001);
        report.Attention.CoveragePercent.Should().BeApproximately(50, 0.0001);
        report.Attention.CameraUnavailableSeconds.Should().Be(1_000);
        report.Attention.UnobservedSeconds.Should().Be(2_000);
        report.Attention.ConfidenceAvg.Should().Be(0.8);
        report.Attention.Warning.Should().BeNull();
    }

    [Fact]
    public void Expired_detail_flags_produce_explicit_warnings()
    {
        var report = StudyReport.Empty(Week) with
        {
            SessionCount = 1,
            StudySeconds = 600,
            FocusedSeconds = 300,
            ActivityDetailsExpired = true,
            AttentionDetailsExpired = true
        };

        report.HasWarnings.Should().BeTrue();
        report.Warnings.Should().HaveCount(2)
            .And.Contain(StudyReport.ActivityDetailsExpiredWarning)
            .And.Contain(StudyReport.AttentionDetailsExpiredWarning);
        report.WarningText.Should()
            .Contain(StudyReport.ActivityDetailsExpiredWarning)
            .And.Contain(StudyReport.AttentionDetailsExpiredWarning);
    }

    [Fact]
    public void Blocked_attempts_are_flagged_as_best_effort()
    {
        StudyReport.BlockedAttemptsNote.Should().Contain("best-effort");
    }
}
