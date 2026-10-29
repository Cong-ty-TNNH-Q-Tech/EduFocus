using EduFocus.Domain.Attention;
using EduFocus.Domain.Entities;
using EduFocus.Domain.Enums;
using EduFocus.SqliteDataAccess.Repositories;
using EduFocus.SqliteDataAccess.Tests.TestSupport;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;

namespace EduFocus.SqliteDataAccess.Tests;

public sealed class StudySessionRepositoryTests
{
    private static readonly DateTime Now = new(2026, 10, 29, 8, 0, 0, DateTimeKind.Utc);

    [Fact]
    public async Task Active_session_round_trips_and_completion_clears_it()
    {
        await using var db = new TempDatabase();
        await db.CreateInitializer().InitializeAsync();

        var repository = new SqliteStudySessionRepository(db.Options);
        var session = StudySession.Start(Now, new DateOnly(2026, 10, 29), SessionTrigger.Manual, true);

        await repository.AddAsync(session);

        var active = await repository.GetActiveAsync();
        active.Should().NotBeNull();
        active!.Id.Should().Be(session.Id);
        active.Status.Should().Be(StudySessionStatus.Active);
        active.StartedAtUtc.Should().Be(Now);
        active.LastHeartbeatAtUtc.Should().Be(Now);
        active.Trigger.Should().Be(SessionTrigger.Manual);
        active.MonitoringEnabled.Should().BeTrue();

        session.Complete(Now.AddMinutes(25));
        await repository.UpdateAsync(session);

        (await repository.GetActiveAsync()).Should().BeNull();
        var reloaded = await ReadSessionAsync(db, session.Id.Value);
        reloaded.Status.Should().Be(StudySessionStatus.Completed);
        reloaded.EndedAtUtc.Should().Be(Now.AddMinutes(25));
    }

    [Fact]
    public async Task Recovery_session_is_returned_as_the_only_active_session()
    {
        await using var db = new TempDatabase();
        await db.CreateInitializer().InitializeAsync();

        var repository = new SqliteStudySessionRepository(db.Options);
        var completed = StudySession.Start(
            Now.AddHours(-2),
            new DateOnly(2026, 10, 29),
            SessionTrigger.Manual,
            true);
        completed.Complete(Now.AddHours(-1));
        await repository.AddAsync(completed);

        var recovered = StudySession.Start(
            Now,
            new DateOnly(2026, 10, 29),
            SessionTrigger.Recovery,
            false);
        await repository.AddAsync(recovered);

        var active = await repository.GetActiveAsync();

        active.Should().NotBeNull();
        active!.Id.Should().Be(recovered.Id);
        active.Trigger.Should().Be(SessionTrigger.Recovery);
        active.MonitoringEnabled.Should().BeFalse();
    }

    [Fact]
    public async Task RecordHeartbeat_updates_active_seconds_and_the_last_heartbeat()
    {
        await using var db = new TempDatabase();
        await db.CreateInitializer().InitializeAsync();

        var repository = new SqliteStudySessionRepository(db.Options);
        var session = StudySession.Start(Now, new DateOnly(2026, 10, 29), SessionTrigger.Manual, true);
        await repository.AddAsync(session);

        var written = await repository.RecordHeartbeatAsync(
            session.Id,
            Now.AddSeconds(10),
            activeSeconds: 10);

        written.Should().BeTrue();

        var reloaded = await ReadSessionAsync(db, session.Id.Value);
        reloaded.LastHeartbeatAtUtc.Should().Be(Now.AddSeconds(10));
        reloaded.ActiveSeconds.Should().Be(10);
        reloaded.UpdatedAtUtc.Should().Be(Now.AddSeconds(10));
    }

    [Fact]
    public async Task RecordHeartbeat_rejects_a_timestamp_behind_the_stored_heartbeat()
    {
        await using var db = new TempDatabase();
        await db.CreateInitializer().InitializeAsync();

        var repository = new SqliteStudySessionRepository(db.Options);
        var session = StudySession.Start(Now, new DateOnly(2026, 10, 29), SessionTrigger.Manual, true);
        await repository.AddAsync(session);
        await repository.RecordHeartbeatAsync(session.Id, Now.AddSeconds(10), activeSeconds: 10);

        var written = await repository.RecordHeartbeatAsync(
            session.Id,
            Now.AddSeconds(5),
            activeSeconds: 10);

        written.Should().BeFalse();

        var reloaded = await ReadSessionAsync(db, session.Id.Value);
        reloaded.LastHeartbeatAtUtc.Should().Be(Now.AddSeconds(10));
        reloaded.ActiveSeconds.Should().Be(10);
    }

    [Fact]
    public async Task RecordHeartbeat_does_not_resurrect_a_completed_session()
    {
        await using var db = new TempDatabase();
        await db.CreateInitializer().InitializeAsync();

        var repository = new SqliteStudySessionRepository(db.Options);
        var session = StudySession.Start(Now, new DateOnly(2026, 10, 29), SessionTrigger.Manual, true);
        await repository.AddAsync(session);

        session.Complete(Now.AddMinutes(30));
        await repository.UpdateAsync(session);

        var written = await repository.RecordHeartbeatAsync(
            session.Id,
            Now.AddMinutes(31),
            activeSeconds: 10);

        written.Should().BeFalse();

        var reloaded = await ReadSessionAsync(db, session.Id.Value);
        reloaded.Status.Should().Be(StudySessionStatus.Completed);
        reloaded.EndedAtUtc.Should().Be(Now.AddMinutes(30));
        reloaded.LastHeartbeatAtUtc.Should().Be(Now.AddMinutes(30));
        reloaded.ActiveSeconds.Should().Be(0);
    }

    [Fact]
    public async Task IncrementBlockedAttemptCount_only_updates_an_active_session()
    {
        await using var db = new TempDatabase();
        await db.CreateInitializer().InitializeAsync();

        var repository = new SqliteStudySessionRepository(db.Options);
        var session = StudySession.Start(Now, new DateOnly(2026, 10, 29), SessionTrigger.Manual, true);
        await repository.AddAsync(session);

        var incremented = await repository.IncrementBlockedAttemptCountAsync(
            session.Id,
            Now.AddSeconds(5),
            count: 2);

        incremented.Should().BeTrue();

        var active = await ReadSessionAsync(db, session.Id.Value);
        active.BlockedAttemptCount.Should().Be(2);
        active.LastHeartbeatAtUtc.Should().Be(Now);

        active.Complete(Now.AddMinutes(30));
        await repository.UpdateAsync(active);

        var afterCompletion = await repository.IncrementBlockedAttemptCountAsync(
            session.Id,
            Now.AddMinutes(31));

        afterCompletion.Should().BeFalse();
        (await ReadSessionAsync(db, session.Id.Value)).BlockedAttemptCount.Should().Be(2);
    }

    [Fact]
    public async Task AddActivitySeconds_updates_category_idle_and_online_class_counters()
    {
        await using var db = new TempDatabase();
        await db.CreateInitializer().InitializeAsync();

        var repository = new SqliteStudySessionRepository(db.Options);
        var session = StudySession.Start(Now, new DateOnly(2026, 10, 29), SessionTrigger.Manual, true);
        await repository.AddAsync(session);

        var written = await repository.AddActivitySecondsAsync(
            session.Id,
            ActivityCategory.Study,
            categorySeconds: 12,
            idleSeconds: 3,
            onlineClassSeconds: 12,
            monitoringEligibleSeconds: 15,
            Now.AddSeconds(15));

        written.Should().BeTrue();

        var reloaded = await ReadSessionAsync(db, session.Id.Value);
        reloaded.StudySeconds.Should().Be(12);
        reloaded.IdleSeconds.Should().Be(3);
        reloaded.OnlineClassSeconds.Should().Be(12);
        reloaded.MonitoringEligibleSeconds.Should().Be(15);
        reloaded.NeutralSeconds.Should().Be(0);
        reloaded.UpdatedAtUtc.Should().Be(Now.AddSeconds(15));

        await repository.AddActivitySecondsAsync(
            session.Id,
            ActivityCategory.Neutral,
            categorySeconds: 10,
            idleSeconds: 0,
            onlineClassSeconds: 0,
            monitoringEligibleSeconds: 0,
            Now.AddSeconds(30));

        reloaded = await ReadSessionAsync(db, session.Id.Value);
        reloaded.NeutralSeconds.Should().Be(10);
        reloaded.StudySeconds.Should().Be(12);
        reloaded.MonitoringEligibleSeconds.Should().Be(15);
    }

    [Fact]
    public async Task AddActivitySeconds_does_not_resurrect_a_completed_session()
    {
        await using var db = new TempDatabase();
        await db.CreateInitializer().InitializeAsync();

        var repository = new SqliteStudySessionRepository(db.Options);
        var session = StudySession.Start(Now, new DateOnly(2026, 10, 29), SessionTrigger.Manual, true);
        await repository.AddAsync(session);

        session.Complete(Now.AddMinutes(30));
        await repository.UpdateAsync(session);

        var written = await repository.AddActivitySecondsAsync(
            session.Id,
            ActivityCategory.Study,
            categorySeconds: 15,
            idleSeconds: 0,
            onlineClassSeconds: 0,
            monitoringEligibleSeconds: 15,
            Now.AddMinutes(31));

        written.Should().BeFalse();

        var reloaded = await ReadSessionAsync(db, session.Id.Value);
        reloaded.Status.Should().Be(StudySessionStatus.Completed);
        reloaded.StudySeconds.Should().Be(0);
        reloaded.UpdatedAtUtc.Should().Be(Now.AddMinutes(30));
    }

    [Fact]
    public async Task A_second_active_session_is_rejected_by_the_unique_index()
    {
        await using var db = new TempDatabase();
        await db.CreateInitializer().InitializeAsync();

        var repository = new SqliteStudySessionRepository(db.Options);
        var first = StudySession.Start(Now, new DateOnly(2026, 10, 29), SessionTrigger.Manual, true);
        await repository.AddAsync(first);

        var second = StudySession.Start(
            Now.AddSeconds(1),
            new DateOnly(2026, 10, 29),
            SessionTrigger.Schedule,
            true);

        var act = () => repository.AddAsync(second);

        await act.Should().ThrowAsync<DbUpdateException>();
        (await repository.GetActiveAsync())!.Id.Should().Be(first.Id);

        await using var context = db.CreateContext();
        (await context.StudySessions.CountAsync(x => x.Status == StudySessionStatus.Active))
            .Should().Be(1);
    }

    [Fact]
    public async Task Deleting_detail_samples_and_events_keeps_the_session_summary()
    {
        await using var db = new TempDatabase();
        await db.CreateInitializer().InitializeAsync();

        var repository = new SqliteStudySessionRepository(db.Options);
        var session = StudySession.Start(Now, new DateOnly(2026, 10, 29), SessionTrigger.Manual, true);
        await repository.AddAsync(session);

        await using (var context = db.CreateContext())
        {
            context.ActivitySamples.Add(ActivitySample.Capture(
                session.Id,
                Now.AddSeconds(1),
                durationSeconds: 10,
                ActivityCategory.Study,
                isForeground: true,
                isOnlineClass: false));
            context.AttentionEvents.Add(AttentionEvent.Open(
                session.Id,
                AttentionState.Focused,
                Now.AddSeconds(1)));
            await context.SaveChangesAsync();
        }

        session.Complete(Now.AddMinutes(30));
        await repository.UpdateAsync(session);

        await using (var context = db.CreateContext())
        {
            (await context.ActivitySamples.ExecuteDeleteAsync()).Should().Be(1);
            (await context.AttentionEvents.ExecuteDeleteAsync()).Should().Be(1);
        }

        await using (var context = db.CreateContext())
        {
            (await context.ActivitySamples.CountAsync()).Should().Be(0);
            (await context.AttentionEvents.CountAsync()).Should().Be(0);
        }

        var summary = await ReadSessionAsync(db, session.Id.Value);
        summary.Status.Should().Be(StudySessionStatus.Completed);
        summary.StartedAtUtc.Should().Be(Now);
        summary.EndedAtUtc.Should().Be(Now.AddMinutes(30));
        summary.MonitoringEnabled.Should().BeTrue();
    }

    [Fact]
    public async Task Attention_seconds_update_counters_and_weighted_confidence()
    {
        await using var db = new TempDatabase();
        await db.CreateInitializer().InitializeAsync();

        var repository = new SqliteStudySessionRepository(db.Options);
        var session = StudySession.Start(Now, new DateOnly(2026, 10, 29), SessionTrigger.Manual, true);
        await repository.AddAsync(session);

        (await repository.AddAttentionSecondsAsync(
            session.Id, AttentionState.Focused, 10, 0.8, Now.AddSeconds(10)))
            .Should().BeTrue();
        (await repository.AddAttentionSecondsAsync(
            session.Id, AttentionState.Distracted, 20, 0.5, Now.AddSeconds(30)))
            .Should().BeTrue();
        (await repository.AddAttentionSecondsAsync(
            session.Id, AttentionState.Absent, 5, 0.4, Now.AddSeconds(40)))
            .Should().BeTrue();
        (await repository.AddAttentionSecondsAsync(
            session.Id, AttentionState.Drowsy, 5, 0.2, Now.AddSeconds(45)))
            .Should().BeTrue();
        (await repository.AddAttentionSecondsAsync(
            session.Id, AttentionState.CameraUnavailable, 30, null, Now.AddSeconds(75)))
            .Should().BeTrue();

        var reloaded = await ReadSessionAsync(db, session.Id.Value);
        reloaded.FocusedSeconds.Should().Be(10);
        reloaded.DistractedSeconds.Should().Be(20);
        reloaded.AbsentSeconds.Should().Be(5);
        reloaded.DrowsySeconds.Should().Be(5);
        reloaded.CameraUnavailableSeconds.Should().Be(30);
        reloaded.MonitoredSeconds.Should().Be(40);
        reloaded.ConfidenceAvg.Should().BeApproximately(0.525, 0.0001);
        reloaded.AttentionPercent.Should().BeApproximately(25, 0.0001);
        reloaded.UpdatedAtUtc.Should().Be(Now.AddSeconds(75));
    }

    [Fact]
    public async Task Attention_seconds_reject_invalid_input_and_inactive_sessions()
    {
        await using var db = new TempDatabase();
        await db.CreateInitializer().InitializeAsync();

        var repository = new SqliteStudySessionRepository(db.Options);
        var session = StudySession.Start(Now, new DateOnly(2026, 10, 29), SessionTrigger.Manual, true);
        await repository.AddAsync(session);

        var negative = () => repository.AddAttentionSecondsAsync(
            session.Id, AttentionState.Focused, -1, 0.5, Now);
        await negative.Should().ThrowAsync<ArgumentOutOfRangeException>();

        var confidence = () => repository.AddAttentionSecondsAsync(
            session.Id, AttentionState.Focused, 1, 1.5, Now);
        await confidence.Should().ThrowAsync<ArgumentOutOfRangeException>();

        var undefined = () => repository.AddAttentionSecondsAsync(
            session.Id, (AttentionState)99, 1, 0.5, Now);
        await undefined.Should().ThrowAsync<ArgumentOutOfRangeException>();

        var localTime = () => repository.AddAttentionSecondsAsync(
            session.Id,
            AttentionState.Focused,
            1,
            0.5,
            DateTime.SpecifyKind(Now, DateTimeKind.Unspecified));
        await localTime.Should().ThrowAsync<ArgumentException>();

        (await repository.AddAttentionSecondsAsync(
            session.Id, AttentionState.Focused, 0, null, Now))
            .Should().BeTrue();

        session.Complete(Now.AddMinutes(30));
        await repository.UpdateAsync(session);

        (await repository.AddAttentionSecondsAsync(
            session.Id, AttentionState.Focused, 5, 0.5, Now.AddMinutes(31)))
            .Should().BeFalse();

        var reloaded = await ReadSessionAsync(db, session.Id.Value);
        reloaded.FocusedSeconds.Should().Be(0);
        reloaded.ConfidenceAvg.Should().BeNull();
    }

    [Fact]
    public async Task Daily_summary_returns_null_when_the_local_date_has_no_sessions()
    {
        await using var db = new TempDatabase();
        await db.CreateInitializer().InitializeAsync();

        var repository = new SqliteStudySessionRepository(db.Options);
        var session = StudySession.Start(Now, new DateOnly(2026, 10, 29), SessionTrigger.Manual, true);
        await repository.AddAsync(session);

        var otherDay = await repository.GetDailySummaryAsync(new DateOnly(2026, 10, 28));
        otherDay.Should().BeNull();

        var invalid = () => repository.GetDailySummaryAsync(default);
        await invalid.Should().ThrowAsync<ArgumentException>();
    }

    [Fact]
    public async Task Daily_summary_aggregates_sessions_and_ignores_other_local_dates()
    {
        await using var db = new TempDatabase();
        await db.CreateInitializer().InitializeAsync();

        var repository = new SqliteStudySessionRepository(db.Options);
        var day = new DateOnly(2026, 10, 29);

        var first = BuildSession(
            startedAtUtc: Now.AddHours(-4),
            localDate: day,
            activeSeconds: 600,
            studySeconds: 500,
            focusedSeconds: 300,
            distractedSeconds: 60,
            eligibleSeconds: 700,
            blockedAttempts: 2);
        first.Complete(Now.AddHours(-3));
        await repository.AddAsync(first);

        var active = BuildSession(
            startedAtUtc: Now.AddHours(-1),
            localDate: day,
            activeSeconds: 300,
            studySeconds: 0,
            focusedSeconds: 100,
            distractedSeconds: 0,
            eligibleSeconds: 200);
        await repository.AddAsync(active);

        var otherDay = BuildSession(
            startedAtUtc: Now.AddDays(-1),
            localDate: new DateOnly(2026, 10, 28),
            activeSeconds: 900,
            studySeconds: 900,
            focusedSeconds: 900,
            distractedSeconds: 0,
            eligibleSeconds: 900);
        otherDay.Complete(Now.AddDays(-1).AddMinutes(30));
        await repository.AddAsync(otherDay);

        var summary = await repository.GetDailySummaryAsync(day);

        summary.Should().NotBeNull();
        summary!.LocalDate.Should().Be(day);
        summary.SessionCount.Should().Be(2);
        summary.ActiveSeconds.Should().Be(900);
        summary.StudySeconds.Should().Be(500);
        summary.DistractionSeconds.Should().Be(0);
        summary.BlockedAttemptCount.Should().Be(2);
        summary.FocusedSeconds.Should().Be(400);
        summary.DistractedSeconds.Should().Be(60);
        summary.MonitoringEligibleSeconds.Should().Be(900);
        summary.CameraUnavailableSeconds.Should().Be(0);
        summary.Attention.AttentionPercent.Should().BeApproximately(400 * 100.0 / 460, 0.0001);
        summary.Attention.CoveragePercent.Should().BeApproximately(460 * 100.0 / 900, 0.0001);
        summary.Attention.IsCoverageLow.Should().BeFalse();
        summary.HasActiveSession.Should().BeTrue();
        summary.ActiveSessionStartedAtUtc.Should().Be(active.StartedAtUtc);
        summary.ActiveSessionStartedAtUtc!.Value.Kind.Should().Be(DateTimeKind.Utc);

        var dayBefore = await repository.GetDailySummaryAsync(new DateOnly(2026, 10, 28));
        dayBefore!.SessionCount.Should().Be(1);
        dayBefore.StudySeconds.Should().Be(900);
    }

    [Fact]
    public async Task Daily_summary_flags_low_coverage_when_the_camera_was_unavailable()
    {
        await using var db = new TempDatabase();
        await db.CreateInitializer().InitializeAsync();

        var repository = new SqliteStudySessionRepository(db.Options);
        var day = new DateOnly(2026, 10, 29);
        var session = StudySession.Start(Now, day, SessionTrigger.Manual, true);
        session.AddMonitoringEligibleSeconds(1_000, Now.AddSeconds(1));
        session.AddAttentionSeconds(AttentionState.Focused, 200, Now.AddSeconds(2));
        session.AddAttentionSeconds(AttentionState.CameraUnavailable, 800, Now.AddSeconds(3));
        session.Complete(Now.AddSeconds(4));
        await repository.AddAsync(session);

        var summary = await repository.GetDailySummaryAsync(day);

        summary!.SessionCount.Should().Be(1);
        summary.CameraUnavailableSeconds.Should().Be(800);
        summary.Attention.MonitoredSeconds.Should().Be(200);
        summary.Attention.AttentionPercent.Should().Be(100);
        summary.Attention.CoveragePercent.Should().Be(20);
        summary.Attention.Warning.Should().Be(SessionAttentionSummary.LowCoverageWarning);
        summary.Attention.UnobservedSeconds.Should().Be(800);
        summary.HasActiveSession.Should().BeFalse();
    }

    [Fact]
    public async Task List_by_local_date_range_returns_only_sessions_inside_the_period()
    {
        await using var db = new TempDatabase();
        await db.CreateInitializer().InitializeAsync();

        var repository = new SqliteStudySessionRepository(db.Options);

        var before = BuildSession(Now.AddDays(-4), new DateOnly(2026, 10, 25), 60, 60, 0, 0, 0);
        before.Complete(Now.AddDays(-4).AddHours(1));
        var first = BuildSession(Now, new DateOnly(2026, 10, 29), 900, 900, 0, 0, 0);
        first.Complete(Now.AddHours(1));
        var second = BuildSession(Now.AddDays(1), new DateOnly(2026, 10, 30), 600, 600, 0, 0, 0);
        second.Complete(Now.AddDays(1).AddHours(1));
        var after = BuildSession(Now.AddDays(4), new DateOnly(2026, 11, 2), 60, 60, 0, 0, 0);
        after.Complete(Now.AddDays(4).AddHours(1));

        await repository.AddAsync(first);
        await repository.AddAsync(before);
        await repository.AddAsync(after);
        await repository.AddAsync(second);

        var sessions = await repository.ListByLocalDateRangeAsync(
            new DateOnly(2026, 10, 29),
            new DateOnly(2026, 10, 30));

        sessions.Should().HaveCount(2);
        sessions[0].Id.Should().Be(first.Id);
        sessions[1].Id.Should().Be(second.Id);
        sessions.Should().OnlyContain(session => session.StartLocalDate >= new DateOnly(2026, 10, 29));
        sessions.Should().OnlyContain(session => session.StartLocalDate <= new DateOnly(2026, 10, 30));
    }

    [Fact]
    public async Task List_by_local_date_range_rejects_invalid_periods()
    {
        await using var db = new TempDatabase();
        await db.CreateInitializer().InitializeAsync();

        var repository = new SqliteStudySessionRepository(db.Options);

        var backwards = () => repository.ListByLocalDateRangeAsync(
            new DateOnly(2026, 10, 30),
            new DateOnly(2026, 10, 29));
        await backwards.Should().ThrowAsync<ArgumentException>();

        var missing = () => repository.ListByLocalDateRangeAsync(
            default,
            new DateOnly(2026, 10, 29));
        await missing.Should().ThrowAsync<ArgumentException>();
    }

    private static StudySession BuildSession(
        DateTime startedAtUtc,
        DateOnly localDate,
        int activeSeconds,
        int studySeconds,
        int focusedSeconds,
        int distractedSeconds,
        int eligibleSeconds,
        int blockedAttempts = 0)
    {
        var session = StudySession.Start(startedAtUtc, localDate, SessionTrigger.Manual, true);
        var stamp = startedAtUtc;

        if (activeSeconds > 0)
            session.AddActiveSeconds(activeSeconds, stamp = stamp.AddSeconds(1));
        if (studySeconds > 0)
            session.AddCategorySeconds(ActivityCategory.Study, studySeconds, stamp = stamp.AddSeconds(1));
        if (focusedSeconds > 0)
            session.AddAttentionSeconds(AttentionState.Focused, focusedSeconds, stamp = stamp.AddSeconds(1));
        if (distractedSeconds > 0)
            session.AddAttentionSeconds(AttentionState.Distracted, distractedSeconds, stamp = stamp.AddSeconds(1));
        if (eligibleSeconds > 0)
            session.AddMonitoringEligibleSeconds(eligibleSeconds, stamp = stamp.AddSeconds(1));
        if (blockedAttempts > 0)
            session.IncrementBlockedAttempts(stamp.AddSeconds(1), blockedAttempts);

        return session;
    }

    private static async Task<StudySession> ReadSessionAsync(TempDatabase db, Guid id)
    {
        await using var context = db.CreateContext();
        var session = await context.StudySessions.FindAsync(
            new EduFocus.Domain.ValueObjects.StudySessionId(id));
        return session ?? throw new InvalidOperationException("Session was not found.");
    }
}
