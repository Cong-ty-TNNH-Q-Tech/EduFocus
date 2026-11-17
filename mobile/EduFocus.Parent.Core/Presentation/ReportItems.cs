using System.Globalization;
using EduFocus.Cloud.Contracts.Reports;

namespace EduFocus.Parent.Core.Presentation;

public sealed class ReportDayItem
{
    public ReportDayItem(DailySummaryDto summary, DateOnly today)
    {
        Summary = summary;
        Date = summary.LocalDate;
        IsToday = summary.LocalDate == today;
        DateText = IsToday
            ? $"{VietnameseFormat.DateLong(summary.LocalDate)} (hôm nay)"
            : VietnameseFormat.DateLong(summary.LocalDate);
        StudyText = VietnameseFormat.Duration(summary.StudySeconds);
        AttentionText = VietnameseFormat.Percent(summary.AttentionPercent);
        CoverageText = VietnameseFormat.Percent(summary.CoveragePercent);
        BlockedText = summary.BlockedAttempts.ToString(CultureInfo.InvariantCulture);
        ShowCoverageWarning = summary.CoveragePercent is { } coverage && coverage < 50;
    }

    public DailySummaryDto Summary { get; }

    public DateOnly Date { get; }

    public bool IsToday { get; }

    public string DateText { get; }

    public string StudyText { get; }

    public string AttentionText { get; }

    public string CoverageText { get; }

    public string BlockedText { get; }

    public bool ShowCoverageWarning { get; }

    public string AttentionCoverageText => $"Tập trung {AttentionText} · Độ phủ {CoverageText}";
}

public sealed record ChartBarItem(string Label, long Seconds, bool IsToday);
