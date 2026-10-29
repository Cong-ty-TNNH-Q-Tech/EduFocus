using EduFocus.Domain.Entities;
using EduFocus.Domain.Enums;
using EduFocus.Domain.Scheduling;
using FluentAssertions;

namespace EduFocus.Domain.Tests.Domain;

public class ScheduleWindowTests
{
    private static readonly DateTime Now = new(2026, 10, 29, 7, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void Create_StoresWindowAndMetadata()
    {
        var window = Create();

        window.Days.Should().Be(ScheduleDays.Weekdays);
        window.StartMinutes.Should().Be(480);
        window.EndMinutes.Should().Be(690);
        window.Label.Should().Be("Giờ học");
        window.IsEnabled.Should().BeTrue();
        window.CreatedAtUtc.Should().Be(Now);
    }

    [Fact]
    public void Covers_UsesInclusiveStartAndExclusiveEnd()
    {
        var window = Create();

        window.Covers(DayOfWeek.Monday, 480).Should().BeTrue();
        window.Covers(DayOfWeek.Monday, 479).Should().BeFalse();
        window.Covers(DayOfWeek.Monday, 689).Should().BeTrue();
        window.Covers(DayOfWeek.Monday, 690).Should().BeFalse();
        window.Covers(DayOfWeek.Sunday, 600).Should().BeFalse();
    }

    [Fact]
    public void Covers_SupportsWindowEndingAtMidnight()
    {
        var window = Create(endMinutes: 1440);

        window.Covers(DayOfWeek.Friday, 1439).Should().BeTrue();
    }

    [Fact]
    public void Covers_IgnoresDisabledWindow()
    {
        var window = Create();
        window.SetEnabled(false, Now.AddMinutes(1));

        window.Covers(DayOfWeek.Monday, 500).Should().BeFalse();
    }

    [Fact]
    public void Covers_RejectsInvalidMinute()
    {
        var window = Create();

        var act = () => window.Covers(DayOfWeek.Monday, 1440);

        act.Should().Throw<ArgumentOutOfRangeException>();
    }

    [Theory]
    [InlineData(ScheduleDays.None, 480, 690)]
    [InlineData((ScheduleDays)128, 480, 690)]
    public void Create_RejectsInvalidDayMasks(ScheduleDays days, int start, int end)
    {
        var act = () => Create(days: days, startMinutes: start, endMinutes: end);

        act.Should().Throw<ArgumentOutOfRangeException>().WithParameterName("days");
    }

    [Theory]
    [InlineData(-1, 690)]
    [InlineData(1440, 1440)]
    [InlineData(480, 0)]
    [InlineData(480, 1441)]
    public void Create_RejectsOutOfRangeMinutes(int start, int end)
    {
        var act = () => Create(startMinutes: start, endMinutes: end);

        act.Should().Throw<ArgumentOutOfRangeException>();
    }

    [Theory]
    [InlineData(480, 480)]
    [InlineData(600, 480)]
    public void Create_RejectsZeroOrNegativeDuration(int start, int end)
    {
        var act = () => Create(startMinutes: start, endMinutes: end);

        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void Create_RejectsLongLabel()
    {
        var act = () => Create(label: new string('a', 101));

        act.Should().Throw<ArgumentException>().WithParameterName("label");
    }

    [Fact]
    public void Overlaps_DetectsSameDayTimeIntersection()
    {
        var morning = Create(startMinutes: 480, endMinutes: 600);

        var overlapping = Create(startMinutes: 540, endMinutes: 660);
        var adjacent = Create(startMinutes: 600, endMinutes: 720);
        var otherDay = Create(days: ScheduleDays.Saturday, startMinutes: 480, endMinutes: 600);

        morning.Overlaps(overlapping).Should().BeTrue();
        morning.Overlaps(adjacent).Should().BeFalse();
        morning.Overlaps(otherDay).Should().BeFalse();
    }

    [Fact]
    public void Validator_RejectsOverlappingEnabledWindows()
    {
        var first = Create(startMinutes: 480, endMinutes: 600);
        var second = Create(startMinutes: 540, endMinutes: 660);

        var act = () => ScheduleValidator.EnsureNoOverlaps([first, second]);

        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void Validator_IgnoresDisabledWindows()
    {
        var first = Create(startMinutes: 480, endMinutes: 600);
        var second = Create(startMinutes: 540, endMinutes: 660);
        second.SetEnabled(false, Now.AddMinutes(1));

        var act = () => ScheduleValidator.EnsureNoOverlaps([first, second]);

        act.Should().NotThrow();
    }

    [Fact]
    public void EnsureCanAdd_RejectsOverlapUnlessCandidateIsDisabled()
    {
        var existing = Create(startMinutes: 480, endMinutes: 600);
        var candidate = Create(startMinutes: 540, endMinutes: 660);

        var act = () => ScheduleValidator.EnsureCanAdd([existing], candidate);
        act.Should().Throw<ArgumentException>();

        candidate.SetEnabled(false, Now.AddMinutes(1));
        var disabledAct = () => ScheduleValidator.EnsureCanAdd([existing], candidate);
        disabledAct.Should().NotThrow();
    }

    [Fact]
    public void Reschedule_ValidatesAndUpdates()
    {
        var window = Create();

        window.Reschedule(ScheduleDays.Weekend, 600, 720, "  Cuối tuần  ", Now.AddMinutes(1));

        window.Days.Should().Be(ScheduleDays.Weekend);
        window.StartMinutes.Should().Be(600);
        window.EndMinutes.Should().Be(720);
        window.Label.Should().Be("Cuối tuần");
        window.UpdatedAtUtc.Should().Be(Now.AddMinutes(1));

        var invalid = () => window.Reschedule(ScheduleDays.Weekend, 720, 600, null, Now.AddMinutes(2));
        invalid.Should().Throw<ArgumentException>();
    }

    private static ScheduleWindow Create(
        ScheduleDays days = ScheduleDays.Weekdays,
        int startMinutes = 480,
        int endMinutes = 690,
        string? label = "Giờ học") =>
        ScheduleWindow.Create(days, startMinutes, endMinutes, label, Now);
}
