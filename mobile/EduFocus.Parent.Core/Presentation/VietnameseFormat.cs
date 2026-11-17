using System.Globalization;

namespace EduFocus.Parent.Core.Presentation;

public static class VietnameseFormat
{
    private static readonly string[] Weekdays =
    [
        "Chủ Nhật",
        "Thứ Hai",
        "Thứ Ba",
        "Thứ Tư",
        "Thứ Năm",
        "Thứ Sáu",
        "Thứ Bảy",
    ];

    public static string Duration(long seconds)
    {
        if (seconds <= 0)
            return "0 phút";

        var hours = seconds / 3600;
        var minutes = seconds % 3600 / 60;
        if (hours > 0)
            return minutes == 0 ? $"{hours} giờ" : $"{hours} giờ {minutes} phút";

        return minutes == 0 ? "< 1 phút" : $"{minutes} phút";
    }

    public static string Percent(double? value) =>
        value is null ? "—" : Percent(value.Value);

    public static string Percent(double value) =>
        value.ToString("0.0", CultureInfo.InvariantCulture).Replace('.', ',') + "%";

    public static string DateLong(DateOnly date) =>
        $"{Weekdays[(int)date.DayOfWeek]}, {date.Day:00}/{date.Month:00}";

    public static string DateShort(DateOnly date) => $"{date.Day:00}/{date.Month:00}";

    public static string DateFull(DateOnly date) =>
        $"{date.Day:00}/{date.Month:00}/{date.Year}";

    public static string DateTimeLocal(DateTimeOffset utc, TimeZoneInfo timeZone)
    {
        var local = TimeZoneInfo.ConvertTime(utc, timeZone);
        return $"{local.Hour:00}:{local.Minute:00} {local.Day:00}/{local.Month:00}/{local.Year}";
    }
}
