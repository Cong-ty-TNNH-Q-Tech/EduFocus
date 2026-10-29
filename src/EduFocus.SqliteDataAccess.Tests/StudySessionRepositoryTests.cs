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
            Now.AddSeconds(15));

        written.Should().BeTrue();

        var reloaded = await ReadSessionAsync(db, session.Id.Value);
        reloaded.StudySeconds.Should().Be(12);
        reloaded.IdleSeconds.Should().Be(3);
        reloaded.OnlineClassSeconds.Should().Be(12);
        reloaded.NeutralSeconds.Should().Be(0);
        reloaded.UpdatedAtUtc.Should().Be(Now.AddSeconds(15));

        await repository.AddActivitySecondsAsync(
            session.Id,
            ActivityCategory.Neutral,
            categorySeconds: 10,
            idleSeconds: 0,
            onlineClassSeconds: 0,
            Now.AddSeconds(30));

        reloaded = await ReadSessionAsync(db, session.Id.Value);
        reloaded.NeutralSeconds.Should().Be(10);
        reloaded.StudySeconds.Should().Be(12);
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

    private static async Task<StudySession> ReadSessionAsync(TempDatabase db, Guid id)
    {
        await using var context = db.CreateContext();
        var session = await context.StudySessions.FindAsync(
            new EduFocus.Domain.ValueObjects.StudySessionId(id));
        return session ?? throw new InvalidOperationException("Session was not found.");
    }
}
