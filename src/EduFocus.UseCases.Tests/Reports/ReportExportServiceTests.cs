using System.Globalization;
using System.Text;
using EduFocus.Domain.Attention;
using EduFocus.Domain.Entities;
using EduFocus.Domain.Enums;
using EduFocus.Domain.Reports;
using EduFocus.Domain.ValueObjects;
using EduFocus.Ports.DTOs;
using EduFocus.UseCases.Reports;
using EduFocus.UseCases.Tests.TestSupport;
using FluentAssertions;

namespace EduFocus.UseCases.Tests.Reports;

public sealed class ReportExportServiceTests
{
    private static readonly DateOnly Day = new(2026, 10, 30);
    private static readonly DateTime Start = new(2026, 10, 30, 8, 0, 0, DateTimeKind.Utc);
    private static readonly DateTimeOffset ExportedAt = new(2026, 10, 30, 10, 5, 6, TimeSpan.Zero);

    [Fact]
    public async Task Export_writes_vietnamese_csv_with_totals_matching_the_report()
    {
        var sessions = new FakeStudySessionRepository();
        var samples = new FakeActivitySampleRepository();
        var events = new FakeAttentionEventRepository();

        var first = CreateSession(
            Start,
            Day,
            activeSeconds: 700,
            studySeconds: 600,
            distractionSeconds: 120,
            focusedSeconds: 300,
            distractedSeconds: 60,
            eligibleSeconds: 500,
            blockedAttempts: 2,
            confidence: 0.8);
        var second = CreateSession(
            Start.AddHours(2),
            Day,
            activeSeconds: 500,
            studySeconds: 300,
            neutralSeconds: 60,
            focusedSeconds: 100,
            absentSeconds: 20,
            eligibleSeconds: 200,
            confidence: 0.4);
        sessions.Sessions.Add(first);
        sessions.Sessions.Add(second);
        samples.ReportSamples.Add(Sample(first, Start.AddSeconds(15), 600, ActivityCategory.Study, "Google Chrome", "youtube.com"));
        samples.ReportSamples.Add(Sample(second, Start.AddHours(2).AddSeconds(15), 300, ActivityCategory.Study, "Notepad"));
        events.Events.Add(AttentionEvent.Open(
            first.Id,
            AttentionState.Focused,
            Start.AddSeconds(1),
            confidence: 0.9,
            appName: "Google Chrome",
            domain: DomainName.Parse("youtube.com")));
        events.Events.Add(AttentionEvent.Open(
            second.Id,
            AttentionState.Focused,
            Start.AddHours(2).AddSeconds(1),
            confidence: 0.5));

        var report = await CreateReportAsync(sessions, samples, events);
        var store = new FakeCsvExportStore();

        var result = await CreateService(samples, store).ExportCsvAsync(report);

        store.FileName.Should().Be("EduFocus-bao-cao-20261030-100506.csv");
        result.FileName.Should().Be(store.FileName);
        result.FilePath.Should().Be(store.FilePath);
        result.DayRowCount.Should().Be(1);
        result.SessionRowCount.Should().Be(2);
        result.ActivitySampleRowCount.Should().Be(2);
        result.AttentionEventRowCount.Should().Be(2);
        result.DataRowCount.Should().Be(8);
        result.Warnings.Should().BeEmpty();

        var rows = ParseCsv(store.Content!);
        var header = rows[0];

        header.Should().Contain(["Loại bản ghi", "Ngày", "Học (giây)", "Ghi chú"]);
        header.Should().Contain("Tên miền");

        var summary = SingleRow(rows, "Tổng hợp");
        Cell(header, summary, "Số phiên").Should().Be("2");
        Cell(header, summary, "Hoạt động (giây)").Should().Be(report.ActiveSeconds.ToString(CultureInfo.InvariantCulture));
        Cell(header, summary, "Học (giây)").Should().Be(report.StudySeconds.ToString(CultureInfo.InvariantCulture));
        Cell(header, summary, "Mất tập trung (giây)").Should().Be("120");
        Cell(header, summary, "Trung tính (giây)").Should().Be("60");
        Cell(header, summary, "Tập trung (giây)").Should().Be(report.FocusedSeconds.ToString(CultureInfo.InvariantCulture));
        Cell(header, summary, "Vắng mặt (giây)").Should().Be("20");
        Cell(header, summary, "Thời gian quan sát (giây)").Should().Be("480");
        Cell(header, summary, "Thời gian đủ điều kiện (giây)").Should().Be("700");
        Cell(header, summary, "Độ phủ (%)").Should().Be("68.57");
        Cell(header, summary, "Tập trung (%)").Should().Be("83.33");
        Cell(header, summary, "Độ tin cậy (%)").Should().Be("70");
        Cell(header, summary, "Bị chặn").Should().Be("2");
        Cell(header, summary, "Ghi chú").Should()
            .Contain("Tổng hợp 2 phiên")
            .And.Contain("không có tiêu đề cửa sổ, URL đầy đủ, PIN, mã khôi phục hoặc dữ liệu camera");

        var day = SingleRow(rows, "Ngày");
        Cell(header, day, "Ngày").Should().Be("2026-10-30");
        Cell(header, day, "Số phiên").Should().Be("2");
        Cell(header, day, "Học (giây)").Should().Be("900");

        var sessionRows = RowsOfType(rows, "Phiên học");
        sessionRows.Should().HaveCount(2);
        sessionRows.Select(row => Cell(header, row, "Trạng thái"))
            .Should().OnlyContain(status => status == "Hoàn tất");
        sessionRows.Select(row => Cell(header, row, "Kích hoạt"))
            .Should().OnlyContain(trigger => trigger == "Thủ công");

        var firstSample = RowsOfType(rows, "Mẫu hoạt động")
            .Single(row => Cell(header, row, "Ứng dụng") == "Google Chrome");
        Cell(header, firstSample, "Tên miền").Should().Be("youtube.com");
        Cell(header, firstSample, "Trạng thái").Should().Be("Học");
        Cell(header, firstSample, "Hoạt động (giây)").Should().Be("600");

        var eventRow = RowsOfType(rows, "Sự kiện tập trung")
            .Single(row => Cell(header, row, "Ứng dụng") == "Google Chrome");
        Cell(header, eventRow, "Trạng thái").Should().Be("Tập trung");
        Cell(header, eventRow, "Tên miền").Should().Be("youtube.com");
        Cell(header, eventRow, "Độ tin cậy (%)").Should().Be("90");
        Cell(header, eventRow, "Ghi chú").Should().Be("Đang diễn ra");

        rows.Should().HaveCount(result.DataRowCount + 1);
    }

    [Fact]
    public async Task Export_quotes_commas_quotes_and_newlines_without_losing_text()
    {
        var sessions = new FakeStudySessionRepository();
        var samples = new FakeActivitySampleRepository();
        var events = new FakeAttentionEventRepository();
        var session = CreateSession(Start, Day, studySeconds: 60);
        sessions.Sessions.Add(session);

        const string appName = "Ứng dụng, \"Học\"\nTập";
        samples.ReportSamples.Add(Sample(session, Start.AddSeconds(15), 60, ActivityCategory.Study, appName));

        var report = await CreateReportAsync(sessions, samples, events);
        var store = new FakeCsvExportStore();

        await CreateService(samples, store).ExportCsvAsync(report);

        store.Content.Should().Contain("\"Ứng dụng, \"\"Học\"\"\nTập\"");
        var rows = ParseCsv(store.Content!);
        var header = rows[0];
        var sampleRow = SingleRow(rows, "Mẫu hoạt động");
        Cell(header, sampleRow, "Ứng dụng").Should().Be(appName);
    }

    [Fact]
    public async Task Export_escapes_cells_that_start_with_formula_characters()
    {
        var sessions = new FakeStudySessionRepository();
        var samples = new FakeActivitySampleRepository();
        var events = new FakeAttentionEventRepository();
        var session = CreateSession(Start, Day, studySeconds: 60);
        sessions.Sessions.Add(session);

        string[] appNames = ["=HYPERLINK(\"http://evil\")", "+SUM(1,2)", "-2+3", "@cmd", "  =1+1", "'already safe"];
        for (var index = 0; index < appNames.Length; index++)
        {
            samples.ReportSamples.Add(Sample(
                session,
                Start.AddSeconds(15 + index),
                60,
                ActivityCategory.Study,
                appNames[index]));
        }

        var report = await CreateReportAsync(sessions, samples, events);
        var store = new FakeCsvExportStore();

        await CreateService(samples, store).ExportCsvAsync(report);

        var rows = ParseCsv(store.Content!);
        var header = rows[0];
        var exportedApps = RowsOfType(rows, "Mẫu hoạt động")
            .Select(row => Cell(header, row, "Ứng dụng"))
            .ToArray();

        exportedApps.Should().BeEquivalentTo(
        [
            "'=HYPERLINK(\"http://evil\")",
            "'+SUM(1,2)",
            "'-2+3",
            "'@cmd",
            "'  =1+1",
            "'already safe"
        ]);

        exportedApps.Should().OnlyContain(app => app[0] == '\'');
    }

    [Fact]
    public async Task Export_drops_samples_and_events_outside_the_selected_local_dates()
    {
        var sessions = new FakeStudySessionRepository();
        var samples = new FakeActivitySampleRepository();
        var events = new FakeAttentionEventRepository();
        var session = CreateSession(
            new DateTime(2026, 10, 30, 23, 50, 0, DateTimeKind.Utc),
            Day,
            studySeconds: 60,
            complete: false);
        sessions.Sessions.Add(session);

        samples.ReportSamples.Add(Sample(
            session,
            new DateTime(2026, 10, 30, 23, 55, 0, DateTimeKind.Utc),
            60,
            ActivityCategory.Study,
            "Inside"));
        samples.ReportSamples.Add(Sample(
            session,
            new DateTime(2026, 10, 31, 0, 5, 0, DateTimeKind.Utc),
            60,
            ActivityCategory.Study,
            "Outside"));

        events.Events.Add(AttentionEvent.Open(
            session.Id,
            AttentionState.Focused,
            new DateTime(2026, 10, 30, 23, 56, 0, DateTimeKind.Utc)));
        events.Events.Add(AttentionEvent.Open(
            session.Id,
            AttentionState.Distracted,
            new DateTime(2026, 10, 31, 0, 10, 0, DateTimeKind.Utc)));

        var report = await CreateReportAsync(sessions, samples, events);
        var store = new FakeCsvExportStore();

        var result = await CreateService(samples, store).ExportCsvAsync(report);

        var rows = ParseCsv(store.Content!);
        var header = rows[0];
        var sampleRows = RowsOfType(rows, "Mẫu hoạt động");
        var eventRows = RowsOfType(rows, "Sự kiện tập trung");

        sampleRows.Should().ContainSingle();
        Cell(header, sampleRows[0], "Ứng dụng").Should().Be("Inside");
        eventRows.Should().ContainSingle();
        Cell(header, eventRows[0], "Trạng thái").Should().Be("Tập trung");
        result.ActivitySampleRowCount.Should().Be(1);
        result.AttentionEventRowCount.Should().Be(1);
    }

    [Fact]
    public async Task Export_omits_session_and_attention_notes_and_full_urls()
    {
        var sessions = new FakeStudySessionRepository();
        var samples = new FakeActivitySampleRepository();
        var events = new FakeAttentionEventRepository();
        var session = CreateSession(Start, Day, studySeconds: 60, complete: false);
        session.SetNote("PIN 123456", Start.AddSeconds(2));
        sessions.Sessions.Add(session);

        samples.ReportSamples.Add(Sample(
            session,
            Start.AddSeconds(15),
            60,
            ActivityCategory.Study,
            "Google Chrome",
            "https://class.example.com/room/7?token=abc"));
        events.Events.Add(AttentionEvent.Open(
            session.Id,
            AttentionState.Focused,
            Start.AddSeconds(1),
            note: "Mã khôi phục ABCDEF",
            appName: "Google Chrome",
            domain: DomainName.Parse("class.example.com")));

        var report = await CreateReportAsync(sessions, samples, events);
        var store = new FakeCsvExportStore();

        await CreateService(samples, store).ExportCsvAsync(report);

        var content = store.Content!;
        content.Should().Contain("class.example.com");
        content.Should().NotContain("PIN 123456");
        content.Should().NotContain("ABCDEF");
        content.Should().NotContain("room/7");
        content.Should().NotContain("token=abc");
        content.Should().NotContain("https://");
    }

    [Fact]
    public async Task Empty_report_exports_header_and_summary_without_reading_detail_tables()
    {
        var samples = new FakeActivitySampleRepository();
        var store = new FakeCsvExportStore();
        var report = StudyReport.Empty(ReportPeriod.Day(Day));

        var result = await CreateService(samples, store).ExportCsvAsync(report);

        samples.ReportCalls.Should().Be(0);
        result.DayRowCount.Should().Be(0);
        result.SessionRowCount.Should().Be(0);
        result.ActivitySampleRowCount.Should().Be(0);
        result.AttentionEventRowCount.Should().Be(0);
        result.DataRowCount.Should().Be(1);

        var rows = ParseCsv(store.Content!);
        rows.Should().HaveCount(2);
        var summary = SingleRow(rows, "Tổng hợp");
        Cell(rows[0], summary, "Số phiên").Should().Be("0");
        Cell(rows[0], summary, "Ghi chú").Should().Contain("Không có phiên học");
    }

    [Fact]
    public async Task Store_failures_propagate_to_the_view_model()
    {
        var samples = new FakeActivitySampleRepository();
        var store = new FakeCsvExportStore { ThrowOnSave = true };
        var report = StudyReport.Empty(ReportPeriod.Day(Day));

        var act = () => CreateService(samples, store).ExportCsvAsync(report);

        await act.Should().ThrowAsync<IOException>();
    }

    private static ReportExportService CreateService(
        FakeActivitySampleRepository samples,
        FakeCsvExportStore store,
        TimeZoneInfo? localTimeZone = null) =>
        new(
            samples,
            store,
            new ManualTimeProvider(ExportedAt, localTimeZone ?? TimeZoneInfo.Utc));

    private static async Task<StudyReport> CreateReportAsync(
        FakeStudySessionRepository sessions,
        FakeActivitySampleRepository samples,
        FakeAttentionEventRepository events)
    {
        var service = new ReportService(
            sessions,
            samples,
            events,
            new ManualTimeProvider(ExportedAt, TimeZoneInfo.Utc));

        return await service.GetAsync(ReportPeriod.Day(Day));
    }

    private static StudySession CreateSession(
        DateTime startedAtUtc,
        DateOnly localDate,
        int activeSeconds = 0,
        int studySeconds = 0,
        int distractionSeconds = 0,
        int neutralSeconds = 0,
        int onlineClassSeconds = 0,
        int focusedSeconds = 0,
        int distractedSeconds = 0,
        int absentSeconds = 0,
        int drowsySeconds = 0,
        int eligibleSeconds = 0,
        int blockedAttempts = 0,
        double? confidence = null,
        bool complete = true)
    {
        var session = StudySession.Start(startedAtUtc, localDate, SessionTrigger.Manual, true);
        var stamp = startedAtUtc;

        if (activeSeconds > 0)
            session.AddActiveSeconds(activeSeconds, stamp = stamp.AddSeconds(1));
        if (studySeconds > 0)
            session.AddCategorySeconds(ActivityCategory.Study, studySeconds, stamp = stamp.AddSeconds(1));
        if (distractionSeconds > 0)
            session.AddCategorySeconds(ActivityCategory.Distraction, distractionSeconds, stamp = stamp.AddSeconds(1));
        if (neutralSeconds > 0)
            session.AddCategorySeconds(ActivityCategory.Neutral, neutralSeconds, stamp = stamp.AddSeconds(1));
        if (onlineClassSeconds > 0)
            session.AddOnlineClassSeconds(onlineClassSeconds, stamp = stamp.AddSeconds(1));
        if (focusedSeconds > 0)
            session.AddAttentionSeconds(AttentionState.Focused, focusedSeconds, stamp = stamp.AddSeconds(1));
        if (distractedSeconds > 0)
            session.AddAttentionSeconds(AttentionState.Distracted, distractedSeconds, stamp = stamp.AddSeconds(1));
        if (absentSeconds > 0)
            session.AddAttentionSeconds(AttentionState.Absent, absentSeconds, stamp = stamp.AddSeconds(1));
        if (drowsySeconds > 0)
            session.AddAttentionSeconds(AttentionState.Drowsy, drowsySeconds, stamp = stamp.AddSeconds(1));
        if (eligibleSeconds > 0)
            session.AddMonitoringEligibleSeconds(eligibleSeconds, stamp = stamp.AddSeconds(1));
        if (blockedAttempts > 0)
            session.IncrementBlockedAttempts(stamp = stamp.AddSeconds(1), blockedAttempts);
        if (confidence is { } value)
            session.SetAttentionConfidence(value, stamp = stamp.AddSeconds(1));
        if (complete)
            session.Complete(stamp = stamp.AddSeconds(1));

        return session;
    }

    private static ReportActivitySample Sample(
        StudySession session,
        DateTime sampledAtUtc,
        int seconds,
        ActivityCategory category,
        string? appName = null,
        string? domain = null) =>
        new(
            session.Id,
            sampledAtUtc,
            seconds,
            category,
            appName,
            string.IsNullOrWhiteSpace(domain) ? null : DomainName.Parse(domain));

    private static string Cell(
        IReadOnlyList<string> header,
        IReadOnlyList<string> row,
        string columnName)
    {
        var index = header.ToList().IndexOf(columnName);
        index.Should().BeGreaterThanOrEqualTo(0, "the CSV must expose the '{0}' column", columnName);
        return row[index];
    }

    private static IReadOnlyList<string> SingleRow(IReadOnlyList<IReadOnlyList<string>> rows, string type) =>
        rows.Skip(1).Single(row => row[0] == type);

    private static IReadOnlyList<IReadOnlyList<string>> RowsOfType(
        IReadOnlyList<IReadOnlyList<string>> rows,
        string type) =>
        rows.Skip(1).Where(row => row[0] == type).ToArray();

    private static IReadOnlyList<IReadOnlyList<string>> ParseCsv(string content)
    {
        var rows = new List<IReadOnlyList<string>>();
        var row = new List<string>();
        var field = new StringBuilder();
        var inQuotes = false;

        for (var index = 0; index < content.Length; index++)
        {
            var character = content[index];

            if (inQuotes)
            {
                if (character == '"')
                {
                    if (index + 1 < content.Length && content[index + 1] == '"')
                    {
                        field.Append('"');
                        index++;
                    }
                    else
                    {
                        inQuotes = false;
                    }
                }
                else
                {
                    field.Append(character);
                }

                continue;
            }

            switch (character)
            {
                case '"':
                    inQuotes = true;
                    break;
                case ',':
                    row.Add(field.ToString());
                    field.Clear();
                    break;
                case '\r':
                    if (index + 1 < content.Length && content[index + 1] == '\n')
                        index++;
                    EndRow();
                    break;
                case '\n':
                    EndRow();
                    break;
                default:
                    field.Append(character);
                    break;
            }
        }

        if (field.Length > 0 || row.Count > 0)
            EndRow();

        return rows;

        void EndRow()
        {
            row.Add(field.ToString());
            field.Clear();
            rows.Add(row);
            row = [];
        }
    }
}
