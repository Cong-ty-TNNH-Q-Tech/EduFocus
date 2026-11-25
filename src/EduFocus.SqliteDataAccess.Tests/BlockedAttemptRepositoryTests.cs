using EduFocus.Domain.Entities;
using EduFocus.Domain.Enums;
using EduFocus.SqliteDataAccess.Repositories;
using EduFocus.SqliteDataAccess.Tests.TestSupport;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;

namespace EduFocus.SqliteDataAccess.Tests;

public sealed class BlockedAttemptRepositoryTests
{
    [Fact]
    public async Task AddAsync_persists_domain_and_app_attempts_with_session()
    {
        await using var db = new TempDatabase();
        await db.CreateInitializer().InitializeAsync();

        var now = new DateTime(2026, 10, 29, 8, 30, 0, DateTimeKind.Utc);
        var session = StudySession.Start(
            now,
            DateOnly.FromDateTime(now),
            SessionTrigger.Manual,
            monitoringEnabled: true);

        await using (var context = db.CreateContext())
        {
            context.StudySessions.Add(session);
            await context.SaveChangesAsync();
        }

        var domain = BlockedAttempt.Record(
            now.AddMinutes(1),
            BlockedAttemptTargetType.Domain,
            "chatgpt.com",
            BlockedAttemptObservedVia.BlockPage,
            session.Id);
        var app = BlockedAttempt.Record(
            now.AddMinutes(2),
            BlockedAttemptTargetType.App,
            "discord",
            BlockedAttemptObservedVia.ProcessWatcher,
            session.Id,
            processName: "discord");

        var repository = new SqliteBlockedAttemptRepository(db.ContextFactory);
        await repository.AddAsync(domain);
        await repository.AddAsync(app);

        await using var read = db.CreateContext();
        var stored = await read.BlockedAttempts
            .AsNoTracking()
            .OrderBy(x => x.OccurredAtUtc)
            .ToListAsync();

        stored.Should().HaveCount(2);
        stored[0].TargetType.Should().Be(BlockedAttemptTargetType.Domain);
        stored[0].TargetValue.Should().Be("chatgpt.com");
        stored[0].Domain!.Value.Should().Be("chatgpt.com");
        stored[0].ObservedVia.Should().Be(BlockedAttemptObservedVia.BlockPage);
        stored[0].SessionId.Should().Be(session.Id);
        stored[1].TargetType.Should().Be(BlockedAttemptTargetType.App);
        stored[1].TargetValue.Should().Be("discord");
        stored[1].ProcessName.Should().Be("discord");
        stored[1].WindowTitle.Should().BeNull();
        stored[1].ObservedVia.Should().Be(BlockedAttemptObservedVia.ProcessWatcher);
        stored[1].SessionId.Should().Be(session.Id);
    }

    [Fact]
    public async Task GetLatestWebAttemptCreatedAtUtcAsync_ignores_app_blocks_and_older_rows()
    {
        await using var db = new TempDatabase();
        await db.CreateInitializer().InitializeAsync();

        var now = new DateTime(2026, 11, 17, 9, 0, 0, DateTimeKind.Utc);
        var repository = new SqliteBlockedAttemptRepository(db.ContextFactory);
        await repository.AddAsync(BlockedAttempt.Record(
            now.AddMinutes(1),
            BlockedAttemptTargetType.Domain,
            "chatgpt.com",
            BlockedAttemptObservedVia.BlockPage,
            createdAtUtc: now.AddMinutes(1)));
        await repository.AddAsync(BlockedAttempt.Record(
            now.AddMinutes(2),
            BlockedAttemptTargetType.Url,
            "https://www.facebook.com/feed",
            BlockedAttemptObservedVia.Extension,
            createdAtUtc: now.AddMinutes(2)));
        await repository.AddAsync(BlockedAttempt.Record(
            now.AddMinutes(5),
            BlockedAttemptTargetType.App,
            "discord",
            BlockedAttemptObservedVia.ProcessWatcher,
            createdAtUtc: now.AddMinutes(5)));

        var latest = await repository.GetLatestWebAttemptCreatedAtUtcAsync(now);
        latest.Should().Be(now.AddMinutes(2));

        var afterLatest = await repository.GetLatestWebAttemptCreatedAtUtcAsync(now.AddMinutes(2));
        afterLatest.Should().BeNull();
    }
}
