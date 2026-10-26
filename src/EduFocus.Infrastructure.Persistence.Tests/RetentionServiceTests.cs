using EduFocus.Domain.Attention;
using EduFocus.Domain.Entities;
using EduFocus.Domain.Enums;
using EduFocus.Infrastructure.Persistence;
using EduFocus.Ports.Outbound.Services;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace EduFocus.Infrastructure.Persistence.Tests;

public sealed class RetentionServiceTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 26, 3, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task Retention_deletes_in_batches_and_keeps_active_or_summaries_with_newer_details()
    {
        await using var fixture = await Fixture.Create();
        ConfigureRetention(await fixture.Db.AppSettings.SingleAsync(), activity: 10, events: 20, sessions: 5);

        var expired = CreateEndedSession(Now.AddDays(-30));
        var summaryWithNewDetail = CreateEndedSession(Now.AddDays(-30));
        var active = StudySession.Start(null, StudySessionTrigger.Manual, true, new DateOnly(2026, 9, 26), Now.AddDays(-30));
        fixture.Db.StudySessions.AddRange(expired, summaryWithNewDetail, active);
        var old = Now.AddDays(-29);
        fixture.Db.ActivitySamples.AddRange(Enumerable.Range(0, 1_101).Select(_ => ActivitySample.Create(
            expired.Id, old, 15, old, ActivityCategory.Study, appName: "Word", processName: "winword")));
        fixture.Db.ActivitySamples.Add(ActivitySample.Create(active.Id, old, 15, old, ActivityCategory.Study));
        fixture.Db.ActivitySamples.Add(ActivitySample.Create(summaryWithNewDetail.Id, Now.AddDays(-5), 15, Now.AddDays(-5), ActivityCategory.Study));
        var oldEvent = AttentionEvent.Start(expired.Id, AttentionState.Focused, old, old);
        oldEvent.Close(old.AddSeconds(30));
        fixture.Db.AttentionEvents.Add(oldEvent);
        fixture.Db.AttentionEvents.Add(AttentionEvent.Start(summaryWithNewDetail.Id, AttentionState.Focused, Now.AddDays(-5), Now.AddDays(-5)));
        fixture.Db.BlockedAttempts.Add(BlockedAttempt.Create(expired.Id, old, BlockTargetType.Domain,
            "expired.example", BlockAttemptSource.BlockPage, old, domain: "expired.example"));
        fixture.Db.BlockedAttempts.Add(BlockedAttempt.Create(summaryWithNewDetail.Id, Now.AddDays(-5), BlockTargetType.Domain,
            "recent.example", BlockAttemptSource.BlockPage, Now.AddDays(-5), domain: "recent.example"));
        var oldAudit = Now.AddDays(-400);
        fixture.Db.AuditLogs.Add(AuditLog.Create(oldAudit, AuditActor.System, "OldEvent", "Expired audit.", oldAudit));
        await fixture.Db.SaveChangesAsync();

        var result = await new RetentionService(fixture.Db, new FakeLogger()).RunAsync(Now);

        Assert.Equal(1_102, result.ActivitySamplesDeleted);
        Assert.Equal(1, result.AttentionEventsDeleted);
        Assert.Equal(1, result.BlockedAttemptsDeleted);
        Assert.Equal(1, result.StudySessionsDeleted);
        Assert.Equal(1, result.AuditLogsDeleted);
        fixture.Db.ChangeTracker.Clear();
        Assert.Equal(2, await fixture.Db.StudySessions.CountAsync());
        Assert.Single(await fixture.Db.StudySessions.Where(x => x.Status == StudySessionStatus.Active).ToArrayAsync());
        Assert.Single(await fixture.Db.ActivitySamples.ToArrayAsync());
        Assert.Single(await fixture.Db.AttentionEvents.ToArrayAsync());
        Assert.Single(await fixture.Db.BlockedAttempts.ToArrayAsync());
        Assert.Single(await fixture.Db.AuditLogs.Where(x => x.EventType == "RetentionCompleted").ToArrayAsync());
    }

    [Fact]
    public async Task Retention_uses_updated_settings_and_retries_after_partial_batch_failure()
    {
        await using var fixture = await Fixture.Create();
        ConfigureRetention(await fixture.Db.AppSettings.SingleAsync(), activity: 5, events: 30, sessions: 365);
        var active = StudySession.Start(null, StudySessionTrigger.Manual, true, new DateOnly(2026, 10, 26), Now);
        fixture.Db.StudySessions.Add(active);
        var recent = ActivitySample.Create(active.Id, Now.AddDays(-10), 1, Now.AddDays(-10), ActivityCategory.Study);
        fixture.Db.ActivitySamples.Add(recent);
        var oldEvent = AttentionEvent.Start(active.Id, AttentionState.Focused, Now.AddDays(-40), Now.AddDays(-40));
        oldEvent.Close(Now.AddDays(-40).AddSeconds(30));
        fixture.Db.AttentionEvents.Add(oldEvent);
        await fixture.Db.SaveChangesAsync();
        await fixture.Db.Database.ExecuteSqlRawAsync("CREATE TRIGGER RejectRetentionEventDelete BEFORE DELETE ON AttentionEvents BEGIN SELECT RAISE(ABORT, 'simulated batch failure'); END;");
        var service = new RetentionService(fixture.Db, new FakeLogger());

        await Assert.ThrowsAsync<SqliteException>(() => service.RunAsync(Now));
        fixture.Db.ChangeTracker.Clear();
        Assert.Empty(await fixture.Db.ActivitySamples.ToArrayAsync());
        Assert.Single(await fixture.Db.AttentionEvents.ToArrayAsync());
        Assert.Empty(await fixture.Db.AuditLogs.ToArrayAsync());

        await fixture.Db.Database.ExecuteSqlRawAsync("DROP TRIGGER RejectRetentionEventDelete;");
        ConfigureRetention(await fixture.Db.AppSettings.SingleAsync(), activity: 5, events: 20, sessions: 365);
        await fixture.Db.SaveChangesAsync();
        var result = await service.RunAsync(Now);

        Assert.Equal(0, result.ActivitySamplesDeleted);
        Assert.Equal(1, result.AttentionEventsDeleted);
        fixture.Db.ChangeTracker.Clear();
        Assert.Empty(await fixture.Db.ActivitySamples.ToArrayAsync());
        Assert.Empty(await fixture.Db.AttentionEvents.ToArrayAsync());
        Assert.Equal(StudySessionStatus.Active, (await fixture.Db.StudySessions.SingleAsync()).Status);
        Assert.Single(await fixture.Db.AuditLogs.Where(x => x.EventType == "RetentionCompleted").ToArrayAsync());
    }

    private static StudySession CreateEndedSession(DateTimeOffset started)
    {
        var session = StudySession.Start(null, StudySessionTrigger.Manual, false, new DateOnly(2026, 9, 26), started);
        session.Complete(started.AddHours(1));
        return session;
    }

    private static void ConfigureRetention(AppSettings settings, int activity, int events, int sessions) =>
        settings.Configure(false, false, false, null, 20, 30, 15, activity, events, sessions, false,
            ThemePreference.Dark, LanguagePreference.Vietnamese, true, Now);

    private sealed class FakeLogger : IAppLogger
    {
        public void Info(string message) { }
        public void Warning(string message) { }
        public void Error(string message, Exception? ex = null) { }
    }

    private sealed class Fixture(SqliteConnection connection, AppDbContext db) : IAsyncDisposable
    {
        public AppDbContext Db { get; } = db;
        public static async Task<Fixture> Create()
        {
            var connection = new SqliteConnection("Data Source=:memory:;Foreign Keys=True");
            await connection.OpenAsync();
            var db = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>().UseSqlite(connection).Options);
            await db.Database.MigrateAsync();
            db.AppSettings.Add(AppSettings.Create(Now));
            await db.SaveChangesAsync();
            return new(connection, db);
        }
        public async ValueTask DisposeAsync() { await Db.DisposeAsync(); await connection.DisposeAsync(); }
    }
}
