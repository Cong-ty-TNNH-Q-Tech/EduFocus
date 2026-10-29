using EduFocus.Domain.Attention;
using EduFocus.Domain.Entities;
using EduFocus.Domain.Enums;
using EduFocus.Domain.ValueObjects;
using EduFocus.SqliteDataAccess.Repositories;
using EduFocus.SqliteDataAccess.Tests.TestSupport;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;

namespace EduFocus.SqliteDataAccess.Tests;

public sealed class RetentionRepositoryTests
{
    private static readonly DateTime Now = new(2026, 10, 30, 3, 0, 0, DateTimeKind.Utc);

    [Fact]
    public async Task Activity_samples_expire_except_rows_of_active_sessions()
    {
        await using var db = new TempDatabase();
        await db.CreateInitializer().InitializeAsync();

        var active = StudySession.Start(Now.AddDays(-60), new DateOnly(2026, 8, 31), SessionTrigger.Manual, true);
        var finished = StudySession.Start(Now.AddDays(-70), new DateOnly(2026, 8, 21), SessionTrigger.Manual, true);
        finished.Complete(Now.AddDays(-69));

        await using (var context = db.CreateContext())
        {
            context.StudySessions.AddRange(active, finished);
            await context.SaveChangesAsync();
        }

        var samples = new SqliteActivitySampleRepository(db.Options);
        await samples.AddAsync(Sample(active.Id, Now.AddDays(-40)));
        await samples.AddAsync(Sample(active.Id, Now.AddDays(-1)));
        await samples.AddAsync(Sample(finished.Id, Now.AddDays(-40)));
        await samples.AddAsync(Sample(finished.Id, Now.AddDays(-1)));

        var repository = new SqliteRetentionRepository(db.Options);
        var deleted = await repository.DeleteActivitySamplesBeforeAsync(Now.AddDays(-30), batchSize: 100);

        deleted.Should().Be(1);

        await using var read = db.CreateContext();
        var remaining = await read.ActivitySamples.AsNoTracking()
            .OrderBy(x => x.SampledAtUtc)
            .ToListAsync();

        remaining.Should().HaveCount(3);
        remaining.Should().Contain(x => x.SessionId == active.Id && x.SampledAtUtc == Now.AddDays(-40));
        remaining.Should().NotContain(x => x.SessionId == finished.Id && x.SampledAtUtc == Now.AddDays(-40));
        (await read.StudySessions.CountAsync()).Should().Be(2);
    }

    [Fact]
    public async Task Attention_events_expire_only_when_closed()
    {
        await using var db = new TempDatabase();
        await db.CreateInitializer().InitializeAsync();

        var session = StudySession.Start(Now.AddDays(-100), new DateOnly(2026, 7, 22), SessionTrigger.Manual, true);
        session.Complete(Now.AddDays(-99));

        var oldClosed = AttentionEvent.Open(session.Id, AttentionState.Focused, Now.AddDays(-95));
        oldClosed.Close(Now.AddDays(-95).AddMinutes(5));
        var oldOpen = AttentionEvent.Open(session.Id, AttentionState.Distracted, Now.AddDays(-94));
        var recentClosed = AttentionEvent.Open(session.Id, AttentionState.Focused, Now.AddDays(-1));
        recentClosed.Close(Now.AddDays(-1).AddMinutes(5));

        await using (var context = db.CreateContext())
        {
            context.StudySessions.Add(session);
            context.AttentionEvents.AddRange(oldClosed, oldOpen, recentClosed);
            await context.SaveChangesAsync();
        }

        var repository = new SqliteRetentionRepository(db.Options);
        var deleted = await repository.DeleteAttentionEventsBeforeAsync(Now.AddDays(-90), batchSize: 100);

        deleted.Should().Be(1);

        await using var read = db.CreateContext();
        var remaining = await read.AttentionEvents.AsNoTracking().ToListAsync();

        remaining.Should().HaveCount(2);
        remaining.Should().Contain(x => x.Id == oldOpen.Id);
        remaining.Should().Contain(x => x.Id == recentClosed.Id);
    }

    [Fact]
    public async Task Blocked_attempts_expire_by_occurrence()
    {
        await using var db = new TempDatabase();
        await db.CreateInitializer().InitializeAsync();

        var attempts = new SqliteBlockedAttemptRepository(db.Options);
        await attempts.AddAsync(BlockedAttempt.Record(
            Now.AddDays(-100),
            BlockedAttemptTargetType.Domain,
            "chatgpt.com",
            BlockedAttemptObservedVia.BlockPage));
        await attempts.AddAsync(BlockedAttempt.Record(
            Now.AddDays(-1),
            BlockedAttemptTargetType.Domain,
            "discord.com",
            BlockedAttemptObservedVia.BlockPage));

        var repository = new SqliteRetentionRepository(db.Options);
        var deleted = await repository.DeleteBlockedAttemptsBeforeAsync(Now.AddDays(-90), batchSize: 100);

        deleted.Should().Be(1);

        await using var read = db.CreateContext();
        (await read.BlockedAttempts.CountAsync()).Should().Be(1);
        (await read.BlockedAttempts.SingleAsync()).Domain!.Value.Should().Be("discord.com");
    }

    [Fact]
    public async Task Audit_logs_expire_by_occurrence()
    {
        await using var db = new TempDatabase();
        await db.CreateInitializer().InitializeAsync();

        var audits = new SqliteAuditLogRepository(db.Options);
        await audits.AddAsync(AuditLog.Record(
            AuditActor.Parent,
            AuditEventType.PinChanged,
            "Old audit entry",
            Now.AddDays(-400)));
        await audits.AddAsync(AuditLog.Record(
            AuditActor.Parent,
            AuditEventType.PinChanged,
            "Recent audit entry",
            Now.AddDays(-1)));

        var repository = new SqliteRetentionRepository(db.Options);
        var deleted = await repository.DeleteAuditLogsBeforeAsync(Now.AddDays(-365), batchSize: 100);

        deleted.Should().Be(1);

        await using var read = db.CreateContext();
        (await read.AuditLogs.CountAsync()).Should().Be(1);
        (await read.AuditLogs.SingleAsync()).Description.Should().Be("Recent audit entry");
    }

    [Fact]
    public async Task Finished_sessions_are_deleted_in_batches_and_cascade_details()
    {
        await using var db = new TempDatabase();
        await db.CreateInitializer().InitializeAsync();

        var sessions = new SqliteStudySessionRepository(db.Options);
        var samples = new SqliteActivitySampleRepository(db.Options);
        var attempts = new SqliteBlockedAttemptRepository(db.Options);
        var created = new List<StudySession>();

        for (var index = 0; index < 3; index++)
        {
            var session = StudySession.Start(
                Now.AddDays(-400 - index),
                new DateOnly(2025, 9, 25),
                SessionTrigger.Manual,
                monitoringEnabled: true);
            session.Complete(Now.AddDays(-399 - index));
            await sessions.AddAsync(session);
            created.Add(session);

            await samples.AddAsync(Sample(session.Id, Now.AddDays(-399 - index)));

            var attention = AttentionEvent.Open(session.Id, AttentionState.Focused, Now.AddDays(-399 - index));
            attention.Close(Now.AddDays(-399 - index).AddMinutes(5));

            await using var context = db.CreateContext();
            context.AttentionEvents.Add(attention);
            await context.SaveChangesAsync();
        }

        await attempts.AddAsync(BlockedAttempt.Record(
            Now.AddDays(-399),
            BlockedAttemptTargetType.Domain,
            "chatgpt.com",
            BlockedAttemptObservedVia.BlockPage,
            created[0].Id));

        var repository = new SqliteRetentionRepository(db.Options);
        var cutoff = Now.AddDays(-365);

        (await repository.DeleteFinishedSessionsBeforeAsync(cutoff, batchSize: 2)).Should().Be(2);
        (await repository.DeleteFinishedSessionsBeforeAsync(cutoff, batchSize: 2)).Should().Be(1);
        (await repository.DeleteFinishedSessionsBeforeAsync(cutoff, batchSize: 2)).Should().Be(0);

        await using var read = db.CreateContext();
        (await read.StudySessions.CountAsync()).Should().Be(0);
        (await read.ActivitySamples.CountAsync()).Should().Be(0);
        (await read.AttentionEvents.CountAsync()).Should().Be(0);

        var attempt = await read.BlockedAttempts.SingleAsync();
        attempt.SessionId.Should().BeNull();
    }

    [Fact]
    public async Task Active_and_recent_sessions_survive_the_session_window()
    {
        await using var db = new TempDatabase();
        await db.CreateInitializer().InitializeAsync();

        var active = StudySession.Start(Now.AddDays(-400), new DateOnly(2025, 9, 25), SessionTrigger.Manual, true);
        var recent = StudySession.Start(Now.AddDays(-10), new DateOnly(2026, 10, 20), SessionTrigger.Manual, true);
        recent.Complete(Now.AddDays(-9));

        await using (var context = db.CreateContext())
        {
            context.StudySessions.AddRange(active, recent);
            await context.SaveChangesAsync();
        }

        var repository = new SqliteRetentionRepository(db.Options);
        var deleted = await repository.DeleteFinishedSessionsBeforeAsync(Now.AddDays(-365), batchSize: 100);

        deleted.Should().Be(0);

        await using var read = db.CreateContext();
        (await read.StudySessions.CountAsync()).Should().Be(2);
    }

    [Fact]
    public async Task Vacuum_runs_without_error()
    {
        await using var db = new TempDatabase();
        await db.CreateInitializer().InitializeAsync();

        var repository = new SqliteRetentionRepository(db.Options);

        await repository.Invoking(x => x.VacuumAsync()).Should().NotThrowAsync();
    }

    [Fact]
    public async Task Invalid_cutoff_or_batch_is_rejected()
    {
        await using var db = new TempDatabase();
        await db.CreateInitializer().InitializeAsync();

        var repository = new SqliteRetentionRepository(db.Options);

        await repository
            .Invoking(x => x.DeleteAuditLogsBeforeAsync(new DateTime(2026, 10, 30, 0, 0, 0, DateTimeKind.Local), 10))
            .Should().ThrowAsync<ArgumentException>();

        await repository
            .Invoking(x => x.DeleteAuditLogsBeforeAsync(Now, 0))
            .Should().ThrowAsync<ArgumentOutOfRangeException>();
    }

    private static ActivitySample Sample(StudySessionId sessionId, DateTime sampledAtUtc) =>
        ActivitySample.Capture(
            sessionId,
            sampledAtUtc,
            durationSeconds: 15,
            ActivityCategory.Study,
            isForeground: true,
            isOnlineClass: false);
}
