using EduFocus.Domain.Entities;
using EduFocus.Domain.Enums;
using EduFocus.Domain.Scheduling;
using FluentAssertions;

namespace EduFocus.Domain.Tests.Scheduling;

public class ScheduleClockTests
{
    private static readonly DateTime Now = new(2026, 10, 29, 7, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void ToMinuteOfDay_uses_local_hour_and_minute()
    {
        ScheduleClock.ToMinuteOfDay(new DateTime(2026, 10, 29, 23, 59, 0)).Should().Be(1439);
        ScheduleClock.ToMinuteOfDay(new DateTime(2026, 10, 29, 0, 0, 0)).Should().Be(0);
    }

    [Fact]
    public void FindActive_matches_day_and_inclusive_start_exclusive_end()
    {
        var window = Create(ScheduleDays.Weekdays, 480, 690);

        ScheduleClock.FindActive([window], new DateTime(2026, 10, 26, 8, 0, 0)).Should().Be(window);
        ScheduleClock.FindActive([window], new DateTime(2026, 10, 26, 11, 29, 0)).Should().Be(window);
        ScheduleClock.FindActive([window], new DateTime(2026, 10, 26, 11, 30, 0)).Should().BeNull();
        ScheduleClock.FindActive([window], new DateTime(2026, 10, 26, 7, 59, 0)).Should().BeNull();
        ScheduleClock.FindActive([window], new DateTime(2026, 10, 25, 9, 0, 0)).Should().BeNull();
    }

    [Fact]
    public void FindActive_handles_local_midnight_and_window_ending_at_1440()
    {
        var late = Create(ScheduleDays.Monday, 1, 1440);

        ScheduleClock.FindActive([late], new DateTime(2026, 10, 26, 23, 59, 0)).Should().Be(late);
        ScheduleClock.FindActive([late], new DateTime(2026, 10, 26, 0, 0, 0)).Should().BeNull();
        ScheduleClock.FindActive([late], new DateTime(2026, 10, 27, 0, 0, 0)).Should().BeNull();
    }

    [Fact]
    public void FindActive_ignores_disabled_windows()
    {
        var window = Create(ScheduleDays.Monday, 480, 690);
        window.SetEnabled(false, Now.AddMinutes(1));

        ScheduleClock.FindActive([window], new DateTime(2026, 10, 26, 9, 0, 0)).Should().BeNull();
    }

    [Fact]
    public void GetWindowEndUtc_converts_local_end_to_utc()
    {
        var zone = TimeZoneInfo.CreateCustomTimeZone("EduFocus/Test+7", TimeSpan.FromHours(7), "Test", "Test");
        var window = Create(ScheduleDays.Weekdays, 480, 690);

        var endUtc = ScheduleClock.GetWindowEndUtc(window, new DateOnly(2026, 10, 29), zone);

        endUtc.Should().Be(new DateTime(2026, 10, 29, 4, 30, 0, DateTimeKind.Utc));
        endUtc.Kind.Should().Be(DateTimeKind.Utc);
    }

    [Fact]
    public void GetWindowEndUtc_for_1440_moves_to_next_local_midnight()
    {
        var zone = TimeZoneInfo.CreateCustomTimeZone("EduFocus/Test+7", TimeSpan.FromHours(7), "Test", "Test");
        var window = Create(ScheduleDays.Weekdays, 1380, 1440);

        var endUtc = ScheduleClock.GetWindowEndUtc(window, new DateOnly(2026, 10, 29), zone);

        endUtc.Should().Be(new DateTime(2026, 10, 29, 17, 0, 0, DateTimeKind.Utc));
    }

    [Fact]
    public void GetWindowEndUtc_moves_a_spring_forward_gap_to_the_first_valid_minute()
    {
        var zone = CreateDstZone();
        var window = Create(ScheduleDays.Sunday, 60, 150);

        var endUtc = ScheduleClock.GetWindowEndUtc(window, new DateOnly(2026, 3, 8), zone);

        endUtc.Should().Be(new DateTime(2026, 3, 8, 7, 0, 0, DateTimeKind.Utc));
    }

    [Fact]
    public void GetWindowEndUtc_uses_the_standard_offset_for_an_ambiguous_fall_back_time()
    {
        var zone = CreateDstZone();
        var window = Create(ScheduleDays.Sunday, 30, 90);

        var endUtc = ScheduleClock.GetWindowEndUtc(window, new DateOnly(2026, 11, 1), zone);

        endUtc.Should().Be(new DateTime(2026, 11, 1, 6, 30, 0, DateTimeKind.Utc));
    }

    [Fact]
    public void FindActive_uses_wall_clock_across_dst_transitions()
    {
        var zone = CreateDstZone();

        var beforeSpringForward = TimeZoneInfo.ConvertTimeToUtc(
            new DateTime(2026, 3, 8, 1, 45, 0),
            zone);
        var localBefore = TimeZoneInfo.ConvertTimeFromUtc(beforeSpringForward, zone);
        localBefore.Hour.Should().Be(1);

        var afterFallBack = TimeZoneInfo.ConvertTimeToUtc(
            new DateTime(2026, 11, 1, 1, 15, 0),
            zone);
        var localAfter = TimeZoneInfo.ConvertTimeFromUtc(afterFallBack, zone);
        localAfter.Hour.Should().Be(1);

        var window = Create(ScheduleDays.Sunday, 60, 120);

        ScheduleClock.FindActive([window], localBefore).Should().Be(window);
        ScheduleClock.FindActive([window], localAfter).Should().Be(window);
    }

    private static ScheduleWindow Create(ScheduleDays days, int startMinutes, int endMinutes) =>
        ScheduleWindow.Create(days, startMinutes, endMinutes, null, Now);

    private static TimeZoneInfo CreateDstZone()
    {
        var daylightStart = TimeZoneInfo.TransitionTime.CreateFloatingDateRule(
            new DateTime(1, 1, 1, 2, 0, 0),
            month: 3,
            week: 2,
            DayOfWeek.Sunday);
        var daylightEnd = TimeZoneInfo.TransitionTime.CreateFloatingDateRule(
            new DateTime(1, 1, 1, 2, 0, 0),
            month: 11,
            week: 1,
            DayOfWeek.Sunday);

        var rule = TimeZoneInfo.AdjustmentRule.CreateAdjustmentRule(
            new DateTime(2000, 1, 1),
            new DateTime(2040, 12, 31),
            TimeSpan.FromHours(1),
            daylightStart,
            daylightEnd);

        return TimeZoneInfo.CreateCustomTimeZone(
            "EduFocus/Test-DST",
            TimeSpan.FromHours(-5),
            "EduFocus Test DST",
            "EduFocus Test Standard",
            "EduFocus Test Daylight",
            [rule]);
    }
}
