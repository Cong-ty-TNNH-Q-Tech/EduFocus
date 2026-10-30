using System.Globalization;
using System.Text;
using EduFocus.Domain.Attention;
using EduFocus.Domain.Entities;
using EduFocus.Domain.Enums;
using EduFocus.Domain.Reports;
using EduFocus.Ports.DTOs;

namespace EduFocus.UseCases.Reports;

/// <summary>
/// Builds the parent-facing report CSV (issue #37). One header row and one row
/// per record type keep the file openable in Excel; cells are quoted for
/// commas, quotes and newlines, and leading formula characters are escaped so
/// app names cannot execute spreadsheet formulas.
/// </summary>
internal static class ReportCsvWriter
{
    internal const string SummaryType = "Tổng hợp";
    internal const string DayType = "Ngày";
    internal const string SessionType = "Phiên học";
    internal const string ActivitySampleType = "Mẫu hoạt động";
    internal const string AttentionEventType = "Sự kiện tập trung";

    private static readonly string[] Header =
    [
        "Loại bản ghi",
        "Ngày",
        "Bắt đầu",
        "Kết thúc",
        "Session ID",
        "Trạng thái",
        "Kích hoạt",
        "Số phiên",
        "Ứng dụng",
        "Tên miền",
        "Hoạt động (giây)",
        "Học (giây)",
        "Mất tập trung (giây)",
        "Trung tính (giây)",
        "Lớp trực tuyến (giây)",
        "Tập trung (giây)",
        "Mất tập trung camera (giây)",
        "Vắng mặt (giây)",
        "Buồn ngủ (giây)",
        "Camera không khả dụng (giây)",
        "Thời gian quan sát (giây)",
        "Thời gian đủ điều kiện (giây)",
        "Độ phủ (%)",
        "Tập trung (%)",
        "Độ tin cậy (%)",
        "Bị chặn",
        "Thời lượng sự kiện (giây)",
        "Ghi chú"
    ];

    private static readonly char[] QuotedCharacters = [',', '"', '\r', '\n'];

    public static string Build(
        StudyReport report,
        IReadOnlyList<ReportActivitySample> samples,
        IReadOnlyList<AttentionEvent> events,
        DateTimeOffset exportedAt,
        TimeZoneInfo timeZone,
        IReadOnlyList<string> warnings)
    {
        var builder = new StringBuilder(16 * 1024);
        AppendRow(builder, Header);

        var summary = NewRow(SummaryType, PeriodText(report.Period));
        summary[7] = Number(report.SessionCount);
        summary[10] = Number(report.ActiveSeconds);
        summary[11] = Number(report.StudySeconds);
        summary[12] = Number(report.DistractionSeconds);
        summary[13] = Number(report.NeutralSeconds);
        summary[14] = Number(report.OnlineClassSeconds);
        summary[15] = Number(report.FocusedSeconds);
        summary[16] = Number(report.DistractedSeconds);
        summary[17] = Number(report.AbsentSeconds);
        summary[18] = Number(report.DrowsySeconds);
        summary[19] = Number(report.CameraUnavailableSeconds);
        summary[20] = Number(report.Attention.MonitoredSeconds);
        summary[21] = Number(report.MonitoringEligibleSeconds);
        summary[22] = Percent(report.Attention.CoveragePercent);
        summary[23] = Percent(report.Attention.AttentionPercent);
        summary[24] = Percent(report.ConfidenceAvg is { } summaryConfidence ? summaryConfidence * 100 : null);
        summary[25] = Number(report.BlockedAttemptCount);
        summary[27] = BuildSummaryNote(report, exportedAt, warnings);
        AppendRow(builder, summary);

        foreach (var day in report.Days.Where(day => day.HasSessions))
        {
            var row = NewRow(DayType, DateText(day.LocalDate));
            row[7] = Number(day.SessionCount);
            row[10] = Number(day.ActiveSeconds);
            row[11] = Number(day.StudySeconds);
            row[12] = Number(day.DistractionSeconds);
            row[13] = Number(day.NeutralSeconds);
            row[14] = Number(day.OnlineClassSeconds);
            row[15] = Number(day.FocusedSeconds);
            row[16] = Number(day.DistractedSeconds);
            row[17] = Number(day.AbsentSeconds);
            row[18] = Number(day.DrowsySeconds);
            row[19] = Number(day.CameraUnavailableSeconds);
            row[20] = Number(day.Attention.MonitoredSeconds);
            row[21] = Number(day.MonitoringEligibleSeconds);
            row[22] = Percent(day.Attention.CoveragePercent);
            row[23] = Percent(day.Attention.AttentionPercent);
            row[25] = Number(day.BlockedAttemptCount);
            AppendRow(builder, row);
        }

        foreach (var session in report.Sessions)
        {
            var row = NewRow(SessionType, DateText(session.StartLocalDate));
            row[2] = LocalText(session.StartedAtUtc, timeZone);
            row[3] = session.IsActive ? null : LocalText(session.EndedAtUtc ?? session.LastHeartbeatAtUtc, timeZone);
            row[4] = session.Id.ToString();
            row[5] = StatusTitle(session.Status);
            row[6] = TriggerTitle(session.Trigger);
            row[10] = Number(session.ActiveSeconds);
            row[11] = Number(session.StudySeconds);
            row[12] = Number(session.DistractionSeconds);
            row[13] = Number(session.NeutralSeconds);
            row[14] = Number(session.OnlineClassSeconds);
            row[15] = Number(session.FocusedSeconds);
            row[16] = Number(session.DistractedSeconds);
            row[17] = Number(session.AbsentSeconds);
            row[18] = Number(session.DrowsySeconds);
            row[19] = Number(session.CameraUnavailableSeconds);
            row[20] = Number(session.MonitoredSeconds);
            row[21] = Number(session.MonitoringEligibleSeconds);
            row[22] = Percent(session.CoveragePercent);
            row[23] = Percent(session.AttentionPercent);
            row[24] = Percent(session.ConfidenceAvg is { } sessionConfidence ? sessionConfidence * 100 : null);
            row[25] = Number(session.BlockedAttemptCount);
            AppendRow(builder, row);
        }

        foreach (var sample in samples)
        {
            var local = ToLocal(sample.SampledAtUtc, timeZone);
            var row = NewRow(ActivitySampleType, DateText(DateOnly.FromDateTime(local)));
            row[2] = LocalText(local);
            row[4] = sample.SessionId.ToString();
            row[5] = CategoryTitle(sample.Category);
            row[8] = sample.AppName;
            row[9] = sample.Domain?.Value;
            row[10] = Number(sample.ActiveSeconds);
            AppendRow(builder, row);
        }

        foreach (var attentionEvent in events)
        {
            var localStart = ToLocal(attentionEvent.StartedAtUtc, timeZone);
            var row = NewRow(AttentionEventType, DateText(DateOnly.FromDateTime(localStart)));
            row[2] = LocalText(localStart);
            row[3] = attentionEvent.EndedAtUtc is { } endedAt
                ? LocalText(ToLocal(endedAt, timeZone))
                : null;
            row[4] = attentionEvent.SessionId.ToString();
            row[5] = AttentionStateTitle(attentionEvent.State);
            row[8] = attentionEvent.AppName;
            row[9] = attentionEvent.Domain?.Value;
            row[24] = Percent(attentionEvent.Confidence is { } eventConfidence ? eventConfidence * 100 : null);
            row[26] = Number(attentionEvent.DurationSeconds);
            row[27] = attentionEvent.IsOpen ? "Đang diễn ra" : null;
            AppendRow(builder, row);
        }

        return builder.ToString();
    }

    internal static string EscapeCell(string? value)
    {
        var text = value ?? string.Empty;

        if (StartsWithFormulaTrigger(text))
            text = "'" + text;

        if (text.IndexOfAny(QuotedCharacters) >= 0)
            text = "\"" + text.Replace("\"", "\"\"", StringComparison.Ordinal) + "\"";

        return text;
    }

    private static bool StartsWithFormulaTrigger(string text)
    {
        foreach (var character in text)
        {
            if (character is ' ' or '\t' or '\r' or '\n')
                continue;

            return character is '=' or '+' or '-' or '@';
        }

        return false;
    }

    private static string?[] NewRow(string type, string date)
    {
        var row = new string?[Header.Length];
        row[0] = type;
        row[1] = date;
        return row;
    }

    private static void AppendRow(StringBuilder builder, IReadOnlyList<string?> cells)
    {
        for (var index = 0; index < cells.Count; index++)
        {
            if (index > 0)
                builder.Append(',');

            builder.Append(EscapeCell(cells[index]));
        }

        builder.Append("\r\n");
    }

    private static string BuildSummaryNote(
        StudyReport report,
        DateTimeOffset exportedAt,
        IReadOnlyList<string> warnings)
    {
        var notes = new List<string>
        {
            $"Xuất lúc {exportedAt.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture)}.",
            report.HasSessions
                ? $"Tổng hợp {report.SessionCount} phiên trong kỳ."
                : "Không có phiên học trong khoảng thời gian này.",
            StudyReport.BlockedAttemptsNote,
            "Chỉ gồm tên ứng dụng và tên miền đã làm sạch; không có tiêu đề cửa sổ, URL đầy đủ, PIN, mã khôi phục hoặc dữ liệu camera."
        };

        notes.AddRange(warnings);

        return string.Join(" ", notes);
    }

    private static string PeriodText(ReportPeriod period) =>
        $"{DateText(period.Start)} – {DateText(period.End)}";

    private static string DateText(DateOnly date) =>
        date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

    private static string LocalText(DateTime utc, TimeZoneInfo timeZone) =>
        LocalText(ToLocal(utc, timeZone));

    private static string LocalText(DateTime local) =>
        local.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture);

    private static DateTime ToLocal(DateTime utc, TimeZoneInfo timeZone) =>
        TimeZoneInfo.ConvertTimeFromUtc(AsUtc(utc), timeZone);

    private static DateTime AsUtc(DateTime value) =>
        value.Kind == DateTimeKind.Utc ? value : DateTime.SpecifyKind(value, DateTimeKind.Utc);

    private static string Number(int value) =>
        value.ToString(CultureInfo.InvariantCulture);

    private static string? Percent(double? value) =>
        value?.ToString("0.##", CultureInfo.InvariantCulture);

    private static string StatusTitle(StudySessionStatus status) =>
        status switch
        {
            StudySessionStatus.Active => "Đang diễn ra",
            StudySessionStatus.Completed => "Hoàn tất",
            StudySessionStatus.Aborted => "Đã hủy",
            _ => "Bị gián đoạn"
        };

    private static string TriggerTitle(SessionTrigger trigger) =>
        trigger switch
        {
            SessionTrigger.Schedule => "Theo lịch",
            SessionTrigger.Recovery => "Khôi phục",
            _ => "Thủ công"
        };

    private static string CategoryTitle(ActivityCategory category) =>
        category switch
        {
            ActivityCategory.Study => "Học",
            ActivityCategory.Distraction => "Mất tập trung",
            ActivityCategory.Neutral => "Trung tính",
            _ => "Không rõ"
        };

    private static string AttentionStateTitle(AttentionState state) =>
        state switch
        {
            AttentionState.Focused => "Tập trung",
            AttentionState.Distracted => "Mất tập trung",
            AttentionState.Absent => "Vắng mặt",
            AttentionState.Drowsy => "Buồn ngủ",
            _ => "Camera không khả dụng"
        };
}
