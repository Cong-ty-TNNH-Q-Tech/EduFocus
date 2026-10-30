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

public sealed class ReportServiceTests
{
    private static readonly DateTime Start = new(2026, 10, 30, 8, 0, 0, DateTimeKind.Utc);
    private static readonly DateOnly Day = new(2026, 10, 30);

    [Fact]
    public async Task Day_report_aggregates_sessions_and_daily_rows()
    {
        var sessions = new FakeStudySessionRepository();
        var samples = new FakeActivitySampleRepository();
        var events = new FakeAttentionEventRepository();
        sessions.Sessions.Add(CreateSession(
            Start,
            Day,
            studySeconds: 600,
            distractionSeconds: 120,
            focusedSeconds: 300,
            distractedSeconds: 60,
            eligibleSeconds: 500,
            blockedAttempts: 2,
            confidence: 0.8));
        sessions.Sessions.Add(CreateSession(
            Start.AddHours(2),
            Day,
            studySeconds: 300,
            neutralSeconds: 60,
            focusedSeconds: 100,
            absentSeconds: 20,
            eligibleSeconds: 200,
            confidence: 0.4));
        samples.ReportSamples.Add(Sample(sessions.Sessions[0], Start.AddSeconds(15), 600, ActivityCategory.Study));
        samples.ReportSamples.Add(Sample(sessions.Sessions[1], Start.AddHours(2).AddSeconds(15), 300, ActivityCategory.Study));
        events.Events.Add(AttentionEvent.Open(sessions.Sessions[0].Id, AttentionState.Focused, Start.AddSeconds(1)));
        events.Events.Add(AttentionEvent.Open(sessions.Sessions[1].Id, AttentionState.Focused, Start.AddHours(2).AddSeconds(1)));

        var report = await CreateService(sessions, samples, events).GetAsync(ReportPeriod.Day(Day));

        sessions.RangeRequests.Should().ContainSingle()
            .Which.Should().Be((Day, Day));
        report.SessionCount.Should().Be(2);
        report.StudySeconds.Should().Be(900);
        report.DistractionSeconds.Should().Be(120);
        report.NeutralSeconds.Should().Be(60);
        report.BlockedAttemptCount.Should().Be(2);
        report.FocusedSeconds.Should().Be(400);
        report.DistractedSeconds.Should().Be(60);
        report.AbsentSeconds.Should().Be(20);
        report.MonitoringEligibleSeconds.Should().Be(700);
        report.ConfidenceAvg.Should().BeApproximately(0.7, 0.0001);
        report.Attention.MonitoredSeconds.Should().Be(480);
        report.Attention.AttentionPercent.Should().BeApproximately(83.3333, 0.001);
        report.Attention.CoveragePercent.Should().BeApproximately(68.5714, 0.001);
        report.Days.Should().ContainSingle();
        report.Days[0].SessionCount.Should().Be(2);
        report.Days[0].StudySeconds.Should().Be(900);
        report.Days[0].ActiveSessionStartedAtUtc.Should().BeNull();
        report.Sessions.Should().HaveCount(2);
        report.Sessions[0].StartedAtUtc.Should().Be(Start.AddHours(2));
        report.HasWarnings.Should().BeFalse();
    }

    [Fact]
    public async Task Week_report_keeps_one_row_per_local_day_across_month_and_year()
    {
        var sessions = new FakeStudySessionRepository();
        var samples = new FakeActivitySampleRepository();
        var events = new FakeAttentionEventRepository();

        // Stored local dates decide the report day; the UTC instants are a
        // different date on purpose (evening in UTC, next morning locally).
        sessions.Sessions.Add(CreateSession(
            new DateTime(2026, 12, 27, 17, 0, 0, DateTimeKind.Utc),
            new DateOnly(2026, 12, 28),
            studySeconds: 600));
        sessions.Sessions.Add(CreateSession(
            new DateTime(2026, 12, 31, 17, 0, 0, DateTimeKind.Utc),
            new DateOnly(2027, 1, 1),
            studySeconds: 300));

        var period = ReportPeriod.Week(new DateOnly(2027, 1, 1));
        var report = await CreateService(sessions, samples, events).GetAsync(period);

        sessions.RangeRequests.Should().ContainSingle()
            .Which.Should().Be((new DateOnly(2026, 12, 28), new DateOnly(2027, 1, 3)));
        report.Days.Should().HaveCount(7);
        report.Days[0].LocalDate.Should().Be(new DateOnly(2026, 12, 28));
        report.Days[0].StudySeconds.Should().Be(600);
        report.Days[4].LocalDate.Should().Be(new DateOnly(2027, 1, 1));
        report.Days[4].StudySeconds.Should().Be(300);
        report.Days[6].LocalDate.Should().Be(new DateOnly(2027, 1, 3));
        report.Days[6].SessionCount.Should().Be(0);
        report.StudySeconds.Should().Be(900);
    }

    [Fact]
    public async Task Timeline_uses_local_hours_on_the_repeated_dst_fall_back_hour()
    {
        var sessions = new FakeStudySessionRepository();
        var samples = new FakeActivitySampleRepository();
        var events = new FakeAttentionEventRepository();
        var session = CreateSession(
            new DateTime(2026, 11, 1, 5, 0, 0, DateTimeKind.Utc),
            new DateOnly(2026, 11, 1),
            studySeconds: 120,
            complete: false);
        sessions.Sessions.Add(session);

        // 05:30Z is 01:30 EDT and 06:30Z is 01:30 EST: the repeated local
        // hour must merge into one bucket instead of shifting to 02:00.
        samples.ReportSamples.Add(Sample(session, new DateTime(2026, 11, 1, 5, 30, 0, DateTimeKind.Utc), 60, ActivityCategory.Study));
        samples.ReportSamples.Add(Sample(session, new DateTime(2026, 11, 1, 6, 30, 0, DateTimeKind.Utc), 60, ActivityCategory.Study));
        samples.ReportSamples.Add(Sample(session, new DateTime(2026, 11, 1, 7, 30, 0, DateTimeKind.Utc), 30, ActivityCategory.Distraction));

        var report = await CreateService(sessions, samples, events, EasternWithDst())
            .GetAsync(ReportPeriod.Day(new DateOnly(2026, 11, 1)));

        report.Timeline.Should().HaveCount(2);
        report.Timeline[0].LocalDate.Should().Be(new DateOnly(2026, 11, 1));
        report.Timeline[0].LocalHour.Should().Be(1);
        report.Timeline[0].StudySeconds.Should().Be(120);
        report.Timeline[1].LocalHour.Should().Be(2);
        report.Timeline[1].DistractionSeconds.Should().Be(30);
    }

    [Fact]
    public async Task Timeline_skips_the_missing_local_hour_on_a_spring_forward_day()
    {
        var sessions = new FakeStudySessionRepository();
        var samples = new FakeActivitySampleRepository();
        var events = new FakeAttentionEventRepository();
        var session = CreateSession(
            new DateTime(2026, 3, 8, 6, 0, 0, DateTimeKind.Utc),
            new DateOnly(2026, 3, 8),
            studySeconds: 120,
            complete: false);
        sessions.Sessions.Add(session);

        samples.ReportSamples.Add(Sample(session, new DateTime(2026, 3, 8, 6, 30, 0, DateTimeKind.Utc), 60, ActivityCategory.Study));
        samples.ReportSamples.Add(Sample(session, new DateTime(2026, 3, 8, 7, 30, 0, DateTimeKind.Utc), 60, ActivityCategory.Study));

        var report = await CreateService(sessions, samples, events, EasternWithDst())
            .GetAsync(ReportPeriod.Day(new DateOnly(2026, 3, 8)));

        report.Timeline.Should().HaveCount(2);
        report.Timeline[0].LocalHour.Should().Be(1);
        report.Timeline[1].LocalHour.Should().Be(3);
        report.Timeline.Should().NotContain(bucket => bucket.LocalHour == 2);
    }

    [Fact]
    public async Task Top_activities_prefer_sanitized_domains_then_apps()
    {
        var sessions = new FakeStudySessionRepository();
        var samples = new FakeActivitySampleRepository();
        var events = new FakeAttentionEventRepository();
        var session = CreateSession(Start, Day, studySeconds: 1_000, complete: false);
        sessions.Sessions.Add(session);

        samples.ReportSamples.Add(Sample(session, Start.AddSeconds(15), 600, ActivityCategory.Distraction, "Google Chrome", "youtube.com"));
        samples.ReportSamples.Add(Sample(session, Start.AddSeconds(30), 300, ActivityCategory.Distraction, "Google Chrome", "youtube.com"));
        samples.ReportSamples.Add(Sample(session, Start.AddSeconds(45), 200, ActivityCategory.Study, "Notepad"));
        samples.ReportSamples.Add(Sample(session, Start.AddSeconds(60), 100, ActivityCategory.Unknown));

        var report = await CreateService(sessions, samples, events).GetAsync(ReportPeriod.Day(Day));

        report.TopActivities.Should().HaveCount(3);
        report.TopActivities[0].Should().Be(new TopActivityItem("youtube.com", TopActivityKind.Domain, 900));
        report.TopActivities[1].Should().Be(new TopActivityItem("Notepad", TopActivityKind.App, 200));
        report.TopActivities[2].Should().Be(new TopActivityItem("Không rõ", TopActivityKind.Unknown, 100));
    }

    [Fact]
    public async Task Report_warns_when_a_session_lost_its_detail_data()
    {
        var sessions = new FakeStudySessionRepository();
        var samples = new FakeActivitySampleRepository();
        var events = new FakeAttentionEventRepository();
        var expired = CreateSession(
            Start,
            Day,
            studySeconds: 600,
            focusedSeconds: 300,
            eligibleSeconds: 500);
        var retained = CreateSession(
            Start.AddHours(2),
            Day,
            studySeconds: 300,
            focusedSeconds: 100,
            eligibleSeconds: 200);
        sessions.Sessions.Add(expired);
        sessions.Sessions.Add(retained);

        samples.ReportSamples.Add(Sample(retained, Start.AddHours(2).AddSeconds(15), 300, ActivityCategory.Study));
        var attentionEvent = AttentionEvent.Open(retained.Id, AttentionState.Focused, Start.AddHours(2).AddSeconds(1), confidence: 0.9);
        events.Events.Add(attentionEvent);

        var report = await CreateService(sessions, samples, events).GetAsync(ReportPeriod.Day(Day));

        report.ActivityDetailsExpired.Should().BeTrue();
        report.AttentionDetailsExpired.Should().BeTrue();
        report.HasWarnings.Should().BeTrue();
        report.WarningText.Should()
            .Contain(StudyReport.ActivityDetailsExpiredWarning)
            .And.Contain(StudyReport.AttentionDetailsExpiredWarning);
    }

    [Fact]
    public async Task Report_has_no_retention_warning_when_every_session_kept_its_details()
    {
        var sessions = new FakeStudySessionRepository();
        var samples = new FakeActivitySampleRepository();
        var events = new FakeAttentionEventRepository();
        var session = CreateSession(
            Start,
            Day,
            studySeconds: 600,
            focusedSeconds: 300,
            eligibleSeconds: 500);
        sessions.Sessions.Add(session);
        samples.ReportSamples.Add(Sample(session, Start.AddSeconds(15), 600, ActivityCategory.Study));
        events.Events.Add(AttentionEvent.Open(session.Id, AttentionState.Focused, Start.AddSeconds(1)));

        var report = await CreateService(sessions, samples, events).GetAsync(ReportPeriod.Day(Day));

        report.ActivityDetailsExpired.Should().BeFalse();
        report.AttentionDetailsExpired.Should().BeFalse();
        report.HasWarnings.Should().BeFalse();
        report.WarningText.Should().BeEmpty();
    }

    [Fact]
    public async Task Empty_period_returns_zeroed_days_without_reading_detail_tables()
    {
        var sessions = new FakeStudySessionRepository();
        var samples = new FakeActivitySampleRepository();
        var events = new FakeAttentionEventRepository();

        var period = ReportPeriod.Week(new DateOnly(2026, 10, 30));
        var report = await CreateService(sessions, samples, events).GetAsync(period);

        report.HasSessions.Should().BeFalse();
        report.SessionCount.Should().Be(0);
        report.Days.Should().HaveCount(7);
        report.Days.Should().OnlyContain(day => day.SessionCount == 0);
        report.Sessions.Should().BeEmpty();
        report.Timeline.Should().BeEmpty();
        report.TopActivities.Should().BeEmpty();
        report.AttentionEvents.Should().BeEmpty();
        report.HasWarnings.Should().BeFalse();
        report.Attention.AttentionPercent.Should().BeNull();
        samples.ReportCalls.Should().Be(0);
        events.ReportCalls.Should().Be(0);
    }

    [Fact]
    public async Task Session_list_is_capped_but_totals_include_every_session()
    {
        var sessions = new FakeStudySessionRepository();
        var samples = new FakeActivitySampleRepository();
        var events = new FakeAttentionEventRepository();

        for (var index = 0; index < ReportService.MaxDisplayedSessions + 1; index++)
        {
            sessions.Sessions.Add(CreateSession(
                Start.AddMinutes(index),
                Day,
                studySeconds: 60));
        }

        var report = await CreateService(sessions, samples, events).GetAsync(ReportPeriod.Day(Day));

        report.SessionCount.Should().Be(ReportService.MaxDisplayedSessions + 1);
        report.Sessions.Should().HaveCount(ReportService.MaxDisplayedSessions);
        report.SessionsTruncated.Should().BeTrue();
        report.StudySeconds.Should().Be((ReportService.MaxDisplayedSessions + 1) * 60);
        report.Days[0].SessionCount.Should().Be(ReportService.MaxDisplayedSessions + 1);
    }

    [Fact]
    public async Task Attention_event_list_is_capped_and_marks_truncation()
    {
        var sessions = new FakeStudySessionRepository();
        var samples = new FakeActivitySampleRepository();
        var events = new FakeAttentionEventRepository();
        var session = CreateSession(Start, Day, focusedSeconds: 300, complete: false);
        sessions.Sessions.Add(session);

        for (var index = 0; index < ReportService.MaxAttentionEvents + 1; index++)
        {
            events.Events.Add(AttentionEvent.Open(
                session.Id,
                index % 2 == 0 ? AttentionState.Focused : AttentionState.Distracted,
                Start.AddSeconds(index)));
        }

        var report = await CreateService(sessions, samples, events).GetAsync(ReportPeriod.Day(Day));

        report.AttentionEvents.Should().HaveCount(ReportService.MaxAttentionEvents);
        report.AttentionEventsTruncated.Should().BeTrue();
        report.AttentionEvents[0].StartedAtUtc.Should().Be(Start.AddSeconds(ReportService.MaxAttentionEvents));
        report.AttentionDetailsExpired.Should().BeFalse();
    }

    [Fact]
    public async Task Repository_failures_propagate_to_the_view_model()
    {
        var sessions = new FakeStudySessionRepository { ThrowOnRangeList = true };
        var service = CreateService(
            sessions,
            new FakeActivitySampleRepository(),
            new FakeAttentionEventRepository());

        var act = () => service.GetAsync(ReportPeriod.Day(Day));

        await act.Should().ThrowAsync<InvalidOperationException>();
    }

    [Fact]
    public async Task Missing_period_is_rejected()
    {
        var service = CreateService(
            new FakeStudySessionRepository(),
            new FakeActivitySampleRepository(),
            new FakeAttentionEventRepository());

        var act = () => service.GetAsync(default);

        await act.Should().ThrowAsync<ArgumentException>();
    }

    private static ReportService CreateService(
        FakeStudySessionRepository sessions,
        FakeActivitySampleRepository samples,
        FakeAttentionEventRepository events,
        TimeZoneInfo? localTimeZone = null) =>
        new(
            sessions,
            samples,
            events,
            new ManualTimeProvider(
                new DateTimeOffset(2026, 10, 30, 9, 0, 0, TimeSpan.Zero),
                localTimeZone ?? TimeZoneInfo.Utc));

    private static StudySession CreateSession(
        DateTime startedAtUtc,
        DateOnly localDate,
        int studySeconds = 0,
        int distractionSeconds = 0,
        int neutralSeconds = 0,
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

        if (studySeconds > 0)
            session.AddCategorySeconds(ActivityCategory.Study, studySeconds, stamp = stamp.AddSeconds(1));
        if (distractionSeconds > 0)
            session.AddCategorySeconds(ActivityCategory.Distraction, distractionSeconds, stamp = stamp.AddSeconds(1));
        if (neutralSeconds > 0)
            session.AddCategorySeconds(ActivityCategory.Neutral, neutralSeconds, stamp = stamp.AddSeconds(1));
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

    private static TimeZoneInfo EasternWithDst()
    {
        var rule = TimeZoneInfo.AdjustmentRule.CreateAdjustmentRule(
            new DateTime(2020, 1, 1),
            new DateTime(2030, 12, 31),
            TimeSpan.FromHours(1),
            TimeZoneInfo.TransitionTime.CreateFloatingDateRule(
                new DateTime(1, 1, 1, 2, 0, 0), 3, 2, DayOfWeek.Sunday),
            TimeZoneInfo.TransitionTime.CreateFloatingDateRule(
                new DateTime(1, 1, 1, 2, 0, 0), 11, 1, DayOfWeek.Sunday));

        return TimeZoneInfo.CreateCustomTimeZone(
            "EduFocus/Test-Eastern-DST",
            TimeSpan.FromHours(-5),
            "EduFocus Test Eastern",
            "EduFocus Test Standard",
            "EduFocus Test Daylight",
            [rule]);
    }
}
