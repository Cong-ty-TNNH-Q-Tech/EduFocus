using System.Globalization;
using EduFocus.Domain.Enums;
using EduFocus.Ports.DTOs;

namespace EduFocus.WPF.ViewModels;

/// <summary>Read-only list row for a stored schedule window (UC06).</summary>
public sealed class ScheduleWindowRow
{
    public ScheduleWindowRow(ScheduleWindowInfo info)
    {
        ArgumentNullException.ThrowIfNull(info);

        Id = info.Id;
        Days = info.Days;
        StartMinutes = info.StartMinutes;
        EndMinutes = info.EndMinutes;
        Label = info.Label;
        IsEnabled = info.IsEnabled;
    }

    public Guid Id { get; }

    public ScheduleDays Days { get; }

    public int StartMinutes { get; }

    public int EndMinutes { get; }

    public string? Label { get; }

    public bool IsEnabled { get; }

    public string Title => string.IsNullOrWhiteSpace(Label) ? "Khung giờ học" : Label;

    public string Summary =>
        $"{ScheduleText.FormatDays(Days)} · {ScheduleText.FormatMinutes(StartMinutes)}–{ScheduleText.FormatMinutes(EndMinutes)}";

    public string StateText => IsEnabled ? "Đang bật" : "Đang tắt";

    public (Guid, ScheduleDays, int, int, string?, bool) Key =>
        (Id, Days, StartMinutes, EndMinutes, Label, IsEnabled);
}

/// <summary>Shared formatting and parsing for schedule windows in the UI.</summary>
internal static class ScheduleText
{
    private static readonly (ScheduleDays Flag, string Short)[] OrderedDays =
    [
        (ScheduleDays.Monday, "T2"),
        (ScheduleDays.Tuesday, "T3"),
        (ScheduleDays.Wednesday, "T4"),
        (ScheduleDays.Thursday, "T5"),
        (ScheduleDays.Friday, "T6"),
        (ScheduleDays.Saturday, "T7"),
        (ScheduleDays.Sunday, "CN")
    ];

    public static string FormatDays(ScheduleDays days)
    {
        if (days == ScheduleDays.None)
            return "Không ngày nào";

        if ((days & ScheduleDays.All) == ScheduleDays.All)
            return "Hằng ngày";

        if (days == ScheduleDays.Weekdays)
            return "T2–T6";

        if (days == ScheduleDays.Weekend)
            return "T7, CN";

        return string.Join(
            ", ",
            OrderedDays.Where(day => (days & day.Flag) != 0).Select(day => day.Short));
    }

    public static string FormatMinutes(int minutes) =>
        minutes == 1440 ? "24:00" : $"{minutes / 60:00}:{minutes % 60:00}";

    public static bool TryParseTime(string? text, bool allowEndOfDay, out int minutes)
    {
        minutes = 0;

        if (string.IsNullOrWhiteSpace(text))
            return false;

        var parts = text.Trim().Split(':');
        if (parts.Length != 2)
            return false;

        if (!int.TryParse(parts[0], NumberStyles.None, CultureInfo.InvariantCulture, out var hours))
            return false;

        if (!int.TryParse(parts[1], NumberStyles.None, CultureInfo.InvariantCulture, out var mins))
            return false;

        if (hours == 24 && allowEndOfDay && mins == 0)
        {
            minutes = 1440;
            return true;
        }

        if (hours is < 0 or > 23 || mins is < 0 or > 59)
            return false;

        minutes = (hours * 60) + mins;
        return true;
    }
}
