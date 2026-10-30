using EduFocus.Domain.Reports;
using FluentAssertions;

namespace EduFocus.Domain.Tests.Reports;

public class ReportPeriodTests
{
    [Fact]
    public void Day_period_contains_exactly_one_date()
    {
        var date = new DateOnly(2026, 10, 30);

        var period = ReportPeriod.Day(date);

        period.Start.Should().Be(date);
        period.End.Should().Be(date);
        period.DayCount.Should().Be(1);
        period.IsSingleDay.Should().BeTrue();
        period.Contains(date).Should().BeTrue();
        period.Contains(date.AddDays(-1)).Should().BeFalse();
        period.EnumerateDays().Should().ContainSingle().Which.Should().Be(date);
    }

    [Fact]
    public void Week_starts_on_monday_and_sunday_belongs_to_the_previous_week()
    {
        var reference = new DateOnly(2026, 11, 1);

        var period = ReportPeriod.Week(reference);

        period.Start.Should().Be(new DateOnly(2026, 10, 26));
        period.End.Should().Be(new DateOnly(2026, 11, 1));
        period.DayCount.Should().Be(7);
        period.Contains(reference).Should().BeTrue();
    }

    [Fact]
    public void Week_can_cross_a_month_boundary()
    {
        var period = ReportPeriod.Week(new DateOnly(2026, 10, 1));

        period.Start.Should().Be(new DateOnly(2026, 9, 28));
        period.End.Should().Be(new DateOnly(2026, 10, 4));
        period.EnumerateDays().Should().HaveCount(7)
            .And.Contain(new DateOnly(2026, 9, 30))
            .And.Contain(new DateOnly(2026, 10, 1));
    }

    [Fact]
    public void Week_can_cross_a_year_boundary()
    {
        var period = ReportPeriod.Week(new DateOnly(2027, 1, 1));

        period.Start.Should().Be(new DateOnly(2026, 12, 28));
        period.End.Should().Be(new DateOnly(2027, 1, 3));
        period.EnumerateDays().Should().HaveCount(7)
            .And.Contain(new DateOnly(2026, 12, 31))
            .And.Contain(new DateOnly(2027, 1, 1));
    }

    [Fact]
    public void Range_accepts_at_most_thirty_one_days()
    {
        var start = new DateOnly(2026, 10, 1);

        var allowed = ReportPeriod.Range(start, start.AddDays(30));
        allowed.DayCount.Should().Be(ReportPeriod.MaxDetailedDays);
        allowed.EnumerateDays().Should().HaveCount(ReportPeriod.MaxDetailedDays);

        var act = () => ReportPeriod.Range(start, start.AddDays(31));
        act.Should().Throw<ArgumentOutOfRangeException>();
    }

    [Fact]
    public void Range_rejects_a_backwards_or_missing_period()
    {
        var start = new DateOnly(2026, 10, 30);

        var backwards = () => ReportPeriod.Range(start, start.AddDays(-1));
        backwards.Should().Throw<ArgumentException>();

        var missing = () => ReportPeriod.Range(default, start);
        missing.Should().Throw<ArgumentException>();

        var dayMissing = () => ReportPeriod.Day(default);
        dayMissing.Should().Throw<ArgumentException>();
    }
}
