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

        await recorder.RecordBlockedAppAsync("blocked.exe", Now.AddSeconds(3));

        var attempt = await fixture.Db.BlockedAttempts.SingleAsync();
        Assert.Equal(session.Id, attempt.SessionId);
        Assert.Equal(BlockTargetType.App, attempt.TargetType);
        Assert.Equal(BlockAttemptSource.ProcessWatcher, attempt.ObservedVia);
        Assert.Equal("blocked.exe", attempt.ProcessName);
        Assert.Null(attempt.Url);
        Assert.Null(attempt.WindowTitle);
    }

    [Fact]
    public async Task Process_watcher_attempt_can_be_recorded_without_an_active_study_session()
    {
        await using var fixture = await Fixture.Create();
        await new BlockedAttemptRecorder(fixture.Db).RecordBlockedAppAsync("blocked.exe", Now);
        Assert.Null((await fixture.Db.BlockedAttempts.SingleAsync()).SessionId);
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
