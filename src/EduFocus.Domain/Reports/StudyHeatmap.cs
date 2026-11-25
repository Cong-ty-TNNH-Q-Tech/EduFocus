namespace EduFocus.Domain.Reports;

/// <summary>
/// One day in the contribution-style heatmap (issue #21). Days outside the
/// requested range are kept as placeholders so the grid always has seven
/// columns per week and the same layout as a GitHub contribution graph.
/// </summary>
public sealed record StudyHeatmapCell
{
    public const int MaxLevel = 4;

    public required DateOnly LocalDate { get; init; }

    public int StudySeconds { get; init; }

    public bool HasSessions { get; init; }

    public bool IsInRange { get; init; }

    /// <summary>
    /// 0 = no study, 1 = under 30 minutes, 2 = under an hour, 3 = under two
    /// hours, 4 = two hours or more. Levels are fixed so the colour keeps the
    /// same meaning every day instead of moving with the busiest day.
    /// </summary>
    public int Level
    {
        get
        {
            if (!IsInRange || StudySeconds <= 0)
                return 0;

            var minutes = StudySeconds / 60;

            if (minutes < 30)
                return 1;
            if (minutes < 60)
                return 2;
            if (minutes < 120)
                return 3;
            return MaxLevel;
        }
    }
}

public sealed record StudyHeatmapWeek(IReadOnlyList<StudyHeatmapCell> Days);

/// <summary>
/// Builds the weekly grid shown on the Parent Mode report screen. Weeks start
/// on Monday to match <see cref="ReportPeriod.Week"/>.
/// </summary>
public static class StudyHeatmap
{
    public const int DefaultDays = 30;

    public static IReadOnlyList<StudyHeatmapWeek> Build(
        IReadOnlyList<DailyStudySummary> days,
        DateOnly firstDate,
        DateOnly lastDate)
    {
        ArgumentNullException.ThrowIfNull(days);

        if (firstDate == default)
            throw new ArgumentException("A heatmap start date is required.", nameof(firstDate));
        if (lastDate == default)
            throw new ArgumentException("A heatmap end date is required.", nameof(lastDate));
        if (lastDate < firstDate)
            throw new ArgumentException("The heatmap end must not precede its start.", nameof(lastDate));

        var summaries = days
            .Where(day => day.LocalDate >= firstDate && day.LocalDate <= lastDate)
            .GroupBy(day => day.LocalDate)
            .ToDictionary(group => group.Key, group => group.First());

        var offsetFromMonday = ((int)firstDate.DayOfWeek + 7 - (int)DayOfWeek.Monday) % 7;
        var gridStart = firstDate.AddDays(-offsetFromMonday);

        var trailingOffset = ((int)DayOfWeek.Sunday + 7 - (int)lastDate.DayOfWeek) % 7;
        var gridEnd = lastDate.AddDays(trailingOffset);

        var cells = new List<StudyHeatmapCell>();
        var weeks = new List<StudyHeatmapWeek>();

        for (var date = gridStart; date <= gridEnd; date = date.AddDays(1))
        {
            var inRange = date >= firstDate && date <= lastDate;
            var summary = inRange && summaries.TryGetValue(date, out var found) ? found : null;

            cells.Add(new StudyHeatmapCell
            {
                LocalDate = date,
                StudySeconds = summary?.StudySeconds ?? 0,
                HasSessions = summary?.HasSessions ?? false,
                IsInRange = inRange
            });

            if (cells.Count == 7)
            {
                weeks.Add(new StudyHeatmapWeek(cells.ToArray()));
                cells.Clear();
            }
        }

        return weeks;
    }
}
