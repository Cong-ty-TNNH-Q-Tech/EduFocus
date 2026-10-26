using EduFocus.Domain.Entities;
using EduFocus.Domain.Enums;
using EduFocus.Infrastructure.Persistence;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace EduFocus.Infrastructure.Persistence.Tests;

public sealed class BlockedAttemptRecorderTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 26, 10, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task Process_watcher_attempt_is_linked_to_active_session_without_path_or_window_title()
    {
        await using var fixture = await Fixture.Create();
        var session = StudySession.Start(null, StudySessionTrigger.Manual, false, new DateOnly(2026, 10, 26), Now);
        fixture.Db.StudySessions.Add(session);
        await fixture.Db.SaveChangesAsync();
        var recorder = new BlockedAttemptRecorder(fixture.Db);

        await recorder.RecordBlockedAppAsync("blocked-session-a.exe", Now.AddSeconds(3));

        var attempt = await fixture.Db.BlockedAttempts.SingleAsync();
        Assert.Equal(session.Id, attempt.SessionId);
        Assert.Equal(BlockTargetType.App, attempt.TargetType);
        Assert.Equal(BlockAttemptSource.ProcessWatcher, attempt.ObservedVia);
        Assert.Equal("blocked-session-a.exe", attempt.ProcessName);
        Assert.Null(attempt.Url);
        Assert.Null(attempt.WindowTitle);
    }

    [Fact]
    public async Task Process_watcher_attempt_can_be_recorded_without_an_active_study_session()
    {
        await using var fixture = await Fixture.Create();
        await new BlockedAttemptRecorder(fixture.Db).RecordBlockedAppAsync("blocked-no-session-b.exe", Now);
        Assert.Null((await fixture.Db.BlockedAttempts.SingleAsync()).SessionId);
    }

    [Fact]
    public async Task Recorder_centralizes_thirty_second_debounce_for_apps_and_block_page_domains()
    {
        await using var fixture = await Fixture.Create();
        var recorder = new BlockedAttemptRecorder(fixture.Db);
        var appName = $"debounce-{Guid.NewGuid():N}.exe";
        var domain = $"debounce-{Guid.NewGuid():N}.example";
        await recorder.RecordBlockedAppAsync(appName, Now);
        await recorder.RecordBlockedAppAsync(appName, Now.AddSeconds(20));
        await recorder.RecordBlockedAppAsync(appName, Now.AddSeconds(31));
        await recorder.RecordBlockedDomainAsync(domain, Now);
        await recorder.RecordBlockedDomainAsync(domain, Now.AddSeconds(20));

        var records = await fixture.Db.BlockedAttempts.OrderBy(x => x.TargetType).ToArrayAsync();
        Assert.Equal(3, records.Length);
        Assert.Equal(2, records.Count(x => x.TargetType == BlockTargetType.App));
        var domainAttempt = Assert.Single(records, x => x.TargetType == BlockTargetType.Domain);
        Assert.Equal(BlockAttemptSource.BlockPage, domainAttempt.ObservedVia);
        Assert.Equal(domain, domainAttempt.Domain);
        Assert.Null(domainAttempt.Url);
        Assert.Null(domainAttempt.WindowTitle);
    }

    private sealed class Fixture(SqliteConnection connection, AppDbContext db) : IAsyncDisposable
    {
        public AppDbContext Db { get; } = db;
        public static async Task<Fixture> Create()
        {
            var connection = new SqliteConnection("Data Source=:memory:");
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
