using EduFocus.Domain.Common;
using EduFocus.Domain.Enums;
using EduFocus.Domain.Privacy;
using EduFocus.Domain.ValueObjects;

namespace EduFocus.Domain.Entities;

/// <summary>
/// A local-time schedule window. Windows never cross midnight and enabled windows
/// must not overlap (database schema §7).
/// </summary>
public sealed class ScheduleWindow
{
    public const int MaxLabelLength = 100;

    private ScheduleWindow(
        ScheduleWindowId id,
        ScheduleDays days,
        int startMinutes,
        int endMinutes,
        string? label,
        bool isEnabled,
        DateTime createdAtUtc)
    {
        Id = id;
        Days = days;
        StartMinutes = startMinutes;
        EndMinutes = endMinutes;
        Label = label;
        IsEnabled = isEnabled;
        CreatedAtUtc = createdAtUtc;
        UpdatedAtUtc = createdAtUtc;
    }

    public ScheduleWindowId Id { get; }

    public ScheduleDays Days { get; private set; }

    public int StartMinutes { get; private set; }

    /// <summary>Exclusive end minute; 1440 means midnight at end of day.</summary>
    public int EndMinutes { get; private set; }

    public string? Label { get; private set; }

    public bool IsEnabled { get; private set; }

    public DateTime CreatedAtUtc { get; }

    public DateTime UpdatedAtUtc { get; private set; }

    public static ScheduleWindow Create(
        ScheduleDays days,
        int startMinutes,
        int endMinutes,
        string? label,
        DateTime nowUtc,
        bool isEnabled = true)
    {
        Validate(days, startMinutes, endMinutes, label, nowUtc);

        return new ScheduleWindow(
            ScheduleWindowId.New(),
            days,
            startMinutes,
            endMinutes,
            TextSanitizer.Clean(label, MaxLabelLength),
            isEnabled,
            nowUtc);
    }

    public bool Covers(DayOfWeek day, int minuteOfDay)
    {
        Guard.InRange(minuteOfDay, 0, 1439, nameof(minuteOfDay));

        return IsEnabled
            && (Days & ToFlag(day)) != 0
            && minuteOfDay >= StartMinutes
            && minuteOfDay < EndMinutes;
    }

    public bool Overlaps(ScheduleWindow other)
    {
        ArgumentNullException.ThrowIfNull(other);

        return (Days & other.Days) != 0
            && StartMinutes < other.EndMinutes
            && other.StartMinutes < EndMinutes;
    }

    public void SetEnabled(bool isEnabled, DateTime nowUtc)
    {
        IsEnabled = isEnabled;
        Touch(nowUtc);
    }

    public void Reschedule(
        ScheduleDays days,
        int startMinutes,
        int endMinutes,
        string? label,
        DateTime nowUtc)
    {
        Validate(days, startMinutes, endMinutes, label, nowUtc);

        Days = days;
        StartMinutes = startMinutes;
        EndMinutes = endMinutes;
        Label = TextSanitizer.Clean(label, MaxLabelLength);
        Touch(nowUtc);
    }

    public static ScheduleDays ToFlag(DayOfWeek day) => day switch
    {
        DayOfWeek.Monday => ScheduleDays.Monday,
        DayOfWeek.Tuesday => ScheduleDays.Tuesday,
        DayOfWeek.Wednesday => ScheduleDays.Wednesday,
        DayOfWeek.Thursday => ScheduleDays.Thursday,
        DayOfWeek.Friday => ScheduleDays.Friday,
        DayOfWeek.Saturday => ScheduleDays.Saturday,
        DayOfWeek.Sunday => ScheduleDays.Sunday,
        _ => throw new ArgumentOutOfRangeException(nameof(day), day, "Unknown day of week.")
    };

    private static void Validate(
        ScheduleDays days,
        int startMinutes,
        int endMinutes,
        string? label,
        DateTime nowUtc)
    {
        Guard.Utc(nowUtc, nameof(nowUtc));

        if (days == ScheduleDays.None || (days & ~ScheduleDays.All) != 0)
            throw new ArgumentOutOfRangeException(nameof(days), days, "At least one known day is required.");

        Guard.InRange(startMinutes, 0, 1439, nameof(startMinutes));
        Guard.InRange(endMinutes, 1, 1440, nameof(endMinutes));

        if (endMinutes <= startMinutes)
            throw new ArgumentException("End must be later than start; overnight windows are not supported.", nameof(endMinutes));

        Guard.MaxLength(label, MaxLabelLength, nameof(label));
    }

    private void Touch(DateTime nowUtc)
    {
        Guard.Utc(nowUtc, nameof(nowUtc));

        if (nowUtc < UpdatedAtUtc)
            throw new ArgumentException("Timestamp must not move backwards.", nameof(nowUtc));

        UpdatedAtUtc = nowUtc;
    }
}
