namespace EduFocus.Parent.Core.Presentation;

public static class FamilyCalendar
{
    public static TimeZoneInfo Resolve(string? timeZoneId) =>
        TimeZoneInfo.TryFindSystemTimeZoneById(timeZoneId ?? string.Empty, out var zone)
            ? zone
            : TimeZoneInfo.Local;

    public static DateOnly Today(DateTimeOffset utcNow, string? timeZoneId) =>
        DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(utcNow, Resolve(timeZoneId)).Date);
}
