using EduFocus.Domain.Reports;
using FluentAssertions;

namespace EduFocus.Domain.Tests.Reports;

public class StudyHeatmapTests
{
    [Fact]
    public void Grid_pads_the_first_and_last_week_with_placeholders()
    {
        var first = new DateOnly(2026, 11, 25);
        var last = new DateOnly(2026, 12, 24);
        var days = Enumerable
            .Range(0, 30)
            .Select(offset => DailyStudySummary.Empty(first.AddDays(offset)))
            .ToArray();

        var weeks = StudyHeatmap.Build(days, first, last);

        weeks.Should().HaveCount(5);
        weeks.Should().OnlyContain(week => week.Days.Count == 7);
        weeks[0].Days[0].LocalDate.DayOfWeek.Should().Be(DayOfWeek.Monday);
        weeks[^1].Days[^1].LocalDate.DayOfWeek.Should().Be(DayOfWeek.Sunday);

        var inRange = weeks.SelectMany(week => week.Days).Where(cell => cell.IsInRange).ToArray();
        inRange.Should().HaveCount(30);
        inRange[0].LocalDate.Should().Be(first);
        inRange[^1].LocalDate.Should().Be(last);
    }

    [Theory]
    [InlineData(0, 0)]
    [InlineData(59, 1)]
    [InlineData(1_800, 2)]
    [InlineData(3_600, 3)]
    [InlineData(7_200, 4)]
    [InlineData(10_800, 4)]
    public void Level_uses_fixed_study_thresholds(int studySeconds, int expectedLevel)
    {
        var cell = new StudyHeatmapCell
        {
            LocalDate = new DateOnly(2026, 11, 25),
            StudySeconds = studySeconds,
            HasSessions = studySeconds > 0,
            IsInRange = true
        };

        cell.Level.Should().Be(expectedLevel);
    }

    [Fact]
    public void Placeholder_days_never_show_a_level()
    {
        var cell = new StudyHeatmapCell
        {
            LocalDate = new DateOnly(2026, 11, 25),
            StudySeconds = 10_800,
            HasSessions = true,
            IsInRange = false
        };

        cell.Level.Should().Be(0);
    }

    [Fact]
    public void Missing_days_are_empty_cells_inside_the_range()
    {
        var first = new DateOnly(2026, 11, 23);
        var last = new DateOnly(2026, 11, 29);
        var days = new[]
        {
            new DailyStudySummary
            {
                LocalDate = new DateOnly(2026, 11, 25),
                SessionCount = 1,
                StudySeconds = 5_400
            }
        };

        var cells = StudyHeatmap.Build(days, first, last).SelectMany(week => week.Days).ToArray();

        cells.Should().HaveCount(7);
        cells[2].LocalDate.Should().Be(new DateOnly(2026, 11, 25));
        cells[2].Level.Should().Be(3);
        cells.Where(cell => cell.LocalDate != new DateOnly(2026, 11, 25))
            .Should().OnlyContain(cell => cell.IsInRange && cell.Level == 0);
    }

    [Fact]
    public void End_before_start_is_rejected()
    {
        var first = new DateOnly(2026, 11, 25);

        var build = () => StudyHeatmap.Build([], first, first.AddDays(-1));

        build.Should().Throw<ArgumentException>();
    }
}
