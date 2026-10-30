using EduFocus.Domain.Attention;
using EduFocus.Domain.Entities;
using EduFocus.Domain.Enums;
using EduFocus.Domain.ValueObjects;
using EduFocus.SqliteDataAccess.Repositories;
using EduFocus.SqliteDataAccess.Tests.TestSupport;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;

namespace EduFocus.SqliteDataAccess.Tests;

public sealed class PrivacyDataRepositoryTests
{
    private static readonly DateTime Now = new(2026, 10, 30, 3, 0, 0, DateTimeKind.Utc);

    [Fact]
    public async Task Counts_and_database_size_reflect_stored_rows()
    {
        await using var db = new TempDatabase();
        await db.CreateInitializer().InitializeAsync();
        var seeded = await SeedAsync(db);

        var repository = new SqlitePrivacyDataRepository(db.ContextFactory);

        var counts = await repository.GetCountsAsync();
        var size = await repository.GetDatabaseSizeBytesAsync();

        counts.Should().Be(new EduFocus.Ports.DTOs.PrivacyDataCounts(3, 1, 4, 2, 2));
        size.Should().BeGreaterThan(0);
        seeded.ActiveSessionId.Should().NotBe(default);
    }

    [Fact]
    public async Task Delete_all_batches_remove_finished_rows_and_keep_active_session_data()
    {
        await using var db = new TempDatabase();
        await db.CreateInitializer().InitializeAsync();
        var seeded = await SeedAsync(db);
        var repository = new SqlitePrivacyDataRepository(db.ContextFactory);

        (await repository.DeleteActivitySamplesBatchAsync(batchSize: 100)).Should().Be(2);
        (await repository.DeleteAttentionEventsBatchAsync(batchSize: 100)).Should().Be(1);
        (await repository.DeleteBlockedAttemptsBatchAsync(batchSize: 100)).Should().Be(2);
        (await repository.DeleteFinishedSessionsBatchAsync(batchSize: 100)).Should().Be(2);
        (await repository.DeleteFinishedSessionsBatchAsync(batchSize: 100)).Should().Be(0);

        await using var read = db.CreateContext();
        (await read.StudySessions.CountAsync()).Should().Be(1);
        (await read.StudySessions.SingleAsync()).Id.Should().Be(seeded.ActiveSessionId);
        (await read.ActivitySamples.CountAsync()).Should().Be(2);
        (await read.ActivitySamples.AllAsync(x => x.SessionId == seeded.ActiveSessionId)).Should().BeTrue();
        (await read.AttentionEvents.CountAsync()).Should().Be(1);
        (await read.BlockedAttempts.CountAsync()).Should().Be(0);

        await repository.VacuumAsync();
    }

    [Fact]
    public async Task Sessions_are_deleted_in_batches_without_touching_active_rows()
    {
        await using var db = new TempDatabase();
        await db.CreateInitializer().InitializeAsync();
        await SeedAsync(db);
        var repository = new SqlitePrivacyDataRepository(db.ContextFactory);

        (await repository.DeleteFinishedSessionsBatchAsync(batchSize: 1)).Should().Be(1);
        (await repository.DeleteFinishedSessionsBatchAsync(batchSize: 1)).Should().Be(1);
        (await repository.DeleteFinishedSessionsBatchAsync(batchSize: 1)).Should().Be(0);

        await using var read = db.CreateContext();
        (await read.StudySessions.CountAsync(x => x.Status == StudySessionStatus.Active)).Should().Be(1);
        (await read.StudySessions.CountAsync()).Should().Be(1);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public async Task Non_positive_batch_is_rejected(int batchSize)
    {
        await using var db = new TempDatabase();
        await db.CreateInitializer().InitializeAsync();
        var repository = new SqlitePrivacyDataRepository(db.ContextFactory);

        var act = () => repository.DeleteActivitySamplesBatchAsync(batchSize);

        await act.Should().ThrowAsync<ArgumentOutOfRangeException>();
    }

    private static async Task<SeededData> SeedAsync(TempDatabase db)
    {
        var active = StudySession.Start(Now.AddHours(-1), new DateOnly(2026, 10, 30), SessionTrigger.Manual, true);
        var first = StudySession.Start(Now.AddDays(-2), new DateOnly(2026, 10, 28), SessionTrigger.Manual, true);
        first.Complete(Now.AddDays(-2).AddHours(1));
        var second = StudySession.Start(Now.AddDays(-3), new DateOnly(2026, 10, 27), SessionTrigger.Manual, true);
        second.Complete(Now.AddDays(-3).AddHours(1));

        await using var context = db.CreateContext();
        context.StudySessions.AddRange(active, first, second);
        context.ActivitySamples.AddRange(
            Sample(active.Id, Now.AddMinutes(-50)),
            Sample(active.Id, Now.AddMinutes(-20)),
            Sample(first.Id, Now.AddDays(-2).AddMinutes(10)),
            Sample(second.Id, Now.AddDays(-3).AddMinutes(10)));

        var activeEvent = AttentionEvent.Open(active.Id, AttentionState.Focused, Now.AddMinutes(-45));
        var finishedEvent = AttentionEvent.Open(first.Id, AttentionState.Distracted, Now.AddDays(-2));
        finishedEvent.Close(Now.AddDays(-2).AddMinutes(1));
        context.AttentionEvents.AddRange(activeEvent, finishedEvent);

        context.BlockedAttempts.AddRange(
            BlockedAttempt.Record(
                Now.AddMinutes(-30),
                BlockedAttemptTargetType.Domain,
                "chatgpt.com",
                BlockedAttemptObservedVia.BlockPage,
                active.Id),
            BlockedAttempt.Record(
                Now.AddDays(-2),
                BlockedAttemptTargetType.Domain,
                "discord.com",
                BlockedAttemptObservedVia.BlockPage));

        await context.SaveChangesAsync();

        return new SeededData(active.Id);
    }

    private static ActivitySample Sample(StudySessionId sessionId, DateTime sampledAtUtc) =>
        ActivitySample.Capture(
            sessionId,
            sampledAtUtc,
            durationSeconds: 15,
            ActivityCategory.Study,
            isForeground: true,
            isOnlineClass: false);

    private sealed record SeededData(StudySessionId ActiveSessionId);
}
