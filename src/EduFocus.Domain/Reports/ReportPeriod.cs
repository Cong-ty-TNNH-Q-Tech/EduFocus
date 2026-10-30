namespace EduFocus.Domain.Reports;

/// <summary>
/// Inclusive local-date range for day/week reports (UC10, issue #36). Sessions
/// are grouped by their stored StartLocalDate, so a DST change never moves a
/// session into another local day (database schema §8).
/// </summary>
public readonly record struct ReportPeriod
{
    /// <summary>
    /// Detailed reports read ActivitySamples, which default to a 30-day
    /// retention; one extra day keeps a full calendar range usable.
    /// </summary>
    public const int MaxDetailedDays = 31;

    private ReportPeriod(DateOnly start, DateOnly end)
    {
        Start = start;
        End = end;
    }

    public DateOnly Start { get; }

    public DateOnly End { get; }

    public int DayCount => End.DayNumber - Start.DayNumber + 1;

    public bool IsSingleDay => Start == End;

    public bool Contains(DateOnly date) => date >= Start && date <= End;

    public static ReportPeriod Day(DateOnly date)
    {
        EnsureDate(date, nameof(date));
        return new ReportPeriod(date, date);
    }

    public static ReportPeriod Week(DateOnly referenceDate)
    {
        EnsureDate(referenceDate, nameof(referenceDate));

        var offsetFromMonday = ((int)referenceDate.DayOfWeek + 7 - (int)DayOfWeek.Monday) % 7;
        var start = referenceDate.AddDays(-offsetFromMonday);
        return new ReportPeriod(start, start.AddDays(6));
    }

    public static ReportPeriod Range(DateOnly start, DateOnly end)
    {
        EnsureDate(start, nameof(start));
        EnsureDate(end, nameof(end));

        if (end < start)
            throw new ArgumentException("The period end must not precede its start.", nameof(end));

        var days = end.DayNumber - start.DayNumber + 1;
        if (days > MaxDetailedDays)
        {
            throw new ArgumentOutOfRangeException(
                nameof(end),
                days,
                $"A detailed report can cover at most {MaxDetailedDays} days.");
        }

        return new ReportPeriod(start, end);
    }

    public IReadOnlyList<DateOnly> EnumerateDays()
    {
        var days = new DateOnly[DayCount];

        for (var index = 0; index < days.Length; index++)
            days[index] = Start.AddDays(index);

        return days;
    }

    private static void EnsureDate(DateOnly date, string parameterName)
    {
        if (date == default)
            throw new ArgumentException("Local date is required.", parameterName);
    }
}
