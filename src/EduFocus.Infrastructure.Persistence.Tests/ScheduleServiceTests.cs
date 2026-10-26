using EduFocus.Domain.Entities;
using EduFocus.Infrastructure.Persistence;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace EduFocus.Infrastructure.Persistence.Tests;

public sealed class ScheduleServiceTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 26, 10, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task CRUD_requires_parent_validates_overlaps_and_audits_changes()
    {
        await using var fixture = await Fixture.Create();
        var service = new ScheduleService(fixture.Db);
        var parent = Parent(Now);
        var window = await service.AddAsync(1, 540, 660, "Study", true, parent, Now);
        await Assert.ThrowsAsync<InvalidOperationException>(() => service.AddAsync(1, 600, 720, "Overlap", true, parent, Now));
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => service.AddAsync(2, 540, 660, null, true, new(), Now));

        await service.UpdateAsync(window.Id, 2, 600, 720, "Updated", true, parent, Now.AddMinutes(1));
        await service.AddAsync(2, 750, 800, "Later", true, parent, Now.AddMinutes(2));
        await Assert.ThrowsAsync<InvalidOperationException>(() => service.UpdateAsync(window.Id, 2, 650, 800, "Bad", true, parent, Now.AddMinutes(3)));
        var persisted = await service.GetWindowsAsync();
        Assert.Equal(2, persisted.Count);
        Assert.Equal(600, persisted.Single(x => x.Id == window.Id).StartMinutes);
        Assert.Equal("Updated", persisted.Single(x => x.Id == window.Id).Label);
        Assert.Equal(3, await fixture.Db.AuditLogs.CountAsync());

        await service.DeleteAsync(window.Id, parent, Now.AddMinutes(3));
        Assert.Single(await service.GetWindowsAsync());
        Assert.Equal(4, await fixture.Db.AuditLogs.CountAsync());
    }

    [Fact]
    public async Task Evaluation_obeys_local_wall_clock_override_expiry_and_dst_fallback()
    {
        await using var fixture = await Fixture.Create();
        var service = new ScheduleService(fixture.Db);
        var pacific = TimeZoneInfo.FindSystemTimeZoneById("Pacific Standard Time");
        await fixture.Db.ScheduleWindows.AddAsync(ScheduleWindow.Create(1 << 6, 60, 120, Now, "Night"));
        await fixture.Db.SaveChangesAsync();

        var firstOneThirty = await service.EvaluateAsync(new DateTimeOffset(2026, 11, 1, 8, 30, 0, TimeSpan.Zero), pacific);
        var repeatedOneThirty = await service.EvaluateAsync(new DateTimeOffset(2026, 11, 1, 9, 30, 0, TimeSpan.Zero), pacific);
        var afterWindow = await service.EvaluateAsync(new DateTimeOffset(2026, 11, 1, 10, 0, 0, TimeSpan.Zero), pacific);
        Assert.True(firstOneThirty.IsScheduled);
        Assert.True(repeatedOneThirty.IsScheduled);
        Assert.False(afterWindow.IsScheduled);

        var overrideAt = new DateTimeOffset(2026, 11, 1, 8, 30, 0, TimeSpan.Zero);
        var parent = Parent(overrideAt);
        var end = await service.OverrideCurrentWindowAsync(parent, overrideAt, pacific);
        Assert.Equal(new DateTimeOffset(2026, 11, 1, 10, 0, 0, TimeSpan.Zero), end);
        var stillOverridden = await service.EvaluateAsync(new DateTimeOffset(2026, 11, 1, 9, 59, 0, TimeSpan.Zero), pacific);
        var expired = await service.EvaluateAsync(new DateTimeOffset(2026, 11, 1, 10, 0, 0, TimeSpan.Zero), pacific);
        Assert.True(stillOverridden.IsOverridden);
        Assert.False(expired.IsOverridden);
        Assert.False(expired.IsScheduled);
    }

    private static EduFocus.Domain.Entities.ParentModeSession Parent(DateTimeOffset at)
    {
        var session = new EduFocus.Domain.Entities.ParentModeSession();
        session.Unlock(at);
        return session;
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
