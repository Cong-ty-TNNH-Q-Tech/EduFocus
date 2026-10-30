using EduFocus.Domain.Entities;

namespace EduFocus.Domain.Scheduling;

/// <summary>
/// Wall-clock helpers for schedule evaluation (UC06). Windows are defined in
/// local time, so DST gaps and overlaps must be resolved explicitly when an
/// override end is converted back to UTC.
/// </summary>
public static class ScheduleClock
{
    public static int ToMinuteOfDay(DateTime localNow) => (localNow.Hour * 60) + localNow.Minute;

    /// <summary>Returns the enabled window covering the local wall-clock instant, if any.</summary>
    public static ScheduleWindow? FindActive(IEnumerable<ScheduleWindow> windows, DateTime localNow)
    {
        ArgumentNullException.ThrowIfNull(windows);

        var minuteOfDay = ToMinuteOfDay(localNow);
        ScheduleWindow? active = null;

        foreach (var window in windows)
        {
            if (!window.Covers(localNow.DayOfWeek, minuteOfDay))
                continue;

            if (active is null || window.EndMinutes < active.EndMinutes)
                active = window;
        }

        return active;
    }

    /// <summary>
    /// Converts the end of <paramref name="window"/> on <paramref name="localDate"/>
    /// to UTC. A nonexistent local end (DST spring-forward gap) moves forward to
    /// the first valid minute; an ambiguous end (fall-back overlap) uses the
    /// standard offset so the override never ends before the repeated hour.
    /// </summary>
    public static DateTime GetWindowEndUtc(ScheduleWindow window, DateOnly localDate, TimeZoneInfo zone)
    {
        ArgumentNullException.ThrowIfNull(window);
        ArgumentNullException.ThrowIfNull(zone);

        return LocalToUtc(localDate.ToDateTime(TimeOnly.MinValue).AddMinutes(window.EndMinutes), zone);
    }

    public static DateTime LocalToUtc(DateTime local, TimeZoneInfo zone)
    {
        ArgumentNullException.ThrowIfNull(zone);

        var wallClock = DateTime.SpecifyKind(local, DateTimeKind.Unspecified);

        while (zone.IsInvalidTime(wallClock))
            wallClock = wallClock.AddMinutes(1);

        // GetUtcOffset returns the standard offset for ambiguous times (fall-back).
        var offset = zone.GetUtcOffset(wallClock);
        return DateTime.SpecifyKind(wallClock - offset, DateTimeKind.Utc);
    }
}
