using EduFocus.Domain.Entities;
using EduFocus.Domain.Enums;
using EduFocus.Infrastructure.Persistence;
using EduFocus.Ports.Outbound.Services;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace EduFocus.Infrastructure.Persistence.Tests;

public sealed class ProtectionCoordinatorTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 26, 10, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task Repeated_enable_disable_is_idempotent_and_updates_state_session_override_and_audit()
    {
        await using var fixture = await Fixture.Create();
        var engine = new FakeProtectionEngine();
        var coordinator = new ProtectionCoordinator(fixture.Db, engine);
        var parent = AuthenticatedSession();

        var enabled = await coordinator.SetProtectionAsync(true, parent, Now.AddSeconds(1));
        Assert.Equal(ProtectionState.On, enabled.State);
        Assert.True(engine.Active);
        Assert.Equal(1, engine.StartCalls);
        Assert.Null((await fixture.Db.AppSettings.SingleAsync()).ScheduleOverrideUntilUtc);
        var session = await fixture.Db.StudySessions.SingleAsync(x => x.Status == StudySessionStatus.Active);
        Assert.Equal(StudySessionTrigger.Manual, session.Trigger);

        await coordinator.SetProtectionAsync(true, parent, Now.AddSeconds(2));
        Assert.Equal(1, engine.StartCalls);
        Assert.Equal(1, await fixture.Db.StudySessions.CountAsync(x => x.Status == StudySessionStatus.Active));
        Assert.Equal(1, await fixture.Db.AuditLogs.CountAsync(x => x.EventType == "ProtectionOn"));

        var disabled = await coordinator.SetProtectionAsync(false, parent, Now.AddSeconds(3));
        Assert.Equal(ProtectionState.Off, disabled.State);
        Assert.False(engine.Active);
        Assert.Equal(StudySessionStatus.Completed, (await fixture.Db.StudySessions.SingleAsync()).Status);
        await coordinator.SetProtectionAsync(false, parent, Now.AddSeconds(4));
        Assert.Equal(1, engine.StopCalls);
        Assert.Equal(1, await fixture.Db.AuditLogs.CountAsync(x => x.EventType == "ProtectionOff"));
    }

    [Fact]
    public async Task Failed_engine_start_rolls_back_resources_and_never_persists_protection_on()
    {
        await using var fixture = await Fixture.Create();
        var engine = new FakeProtectionEngine { FailStart = true };
        var coordinator = new ProtectionCoordinator(fixture.Db, engine);
        await Assert.ThrowsAsync<InvalidOperationException>(() => coordinator.SetProtectionAsync(true, AuthenticatedSession(), Now.AddSeconds(1)));

        Assert.False(engine.Active);
        Assert.Equal(1, engine.ReconcileCalls);
        Assert.Equal(ProtectionState.Off, (await fixture.Db.AppSettings.SingleAsync()).Protection);
        Assert.Empty(await fixture.Db.StudySessions.ToArrayAsync());
        Assert.Equal("ProtectionOnFailed", (await fixture.Db.AuditLogs.SingleAsync()).EventType);
    }

    [Fact]
    public async Task Failed_engine_stop_leaves_database_on_and_reconciles_resources_on()
    {
        await using var fixture = await Fixture.Create();
        var engine = new FakeProtectionEngine();
        var coordinator = new ProtectionCoordinator(fixture.Db, engine);
        var parent = AuthenticatedSession();
        await coordinator.SetProtectionAsync(true, parent, Now.AddSeconds(1));
        engine.FailStop = true;

        await Assert.ThrowsAsync<InvalidOperationException>(() => coordinator.SetProtectionAsync(false, parent, Now.AddSeconds(2)));
        Assert.True(engine.Active);
        Assert.Equal(ProtectionState.On, (await fixture.Db.AppSettings.SingleAsync()).Protection);
        Assert.Equal(StudySessionStatus.Active, (await fixture.Db.StudySessions.SingleAsync()).Status);
        Assert.Empty(await fixture.Db.AuditLogs.Where(x => x.EventType == "ProtectionOff").ToArrayAsync());
        Assert.Equal("ProtectionOffFailed", (await fixture.Db.AuditLogs.SingleAsync(x => x.EventType == "ProtectionOffFailed")).EventType);
    }

    [Fact]
    public async Task Restart_reconciles_engine_interrupts_old_session_and_starts_one_recovery_session()
    {
        await using var fixture = await Fixture.Create();
        var settings = await fixture.Db.AppSettings.SingleAsync();
        settings.SetProtection(ProtectionState.On, Now);
        var previous = StudySession.Start(null, StudySessionTrigger.Manual, true, new DateOnly(2026, 10, 26), Now);
        previous.Heartbeat(Now.AddMinutes(2));
        fixture.Db.StudySessions.Add(previous);
        await fixture.Db.SaveChangesAsync();
        var engine = new FakeProtectionEngine();
        var coordinator = new ProtectionCoordinator(fixture.Db, engine);

        var result = await coordinator.ReconcileAfterRestartAsync(Now.AddMinutes(20));
        Assert.Equal(ProtectionState.On, result.State);
        Assert.True(engine.Active);
        var sessions = await fixture.Db.StudySessions.OrderBy(x => x.StartedAtUtc).ToArrayAsync();
        Assert.Equal(2, sessions.Length);
        Assert.Equal(StudySessionStatus.Interrupted, sessions[0].Status);
        Assert.Equal(Now.AddMinutes(2), sessions[0].EndedAtUtc);
        Assert.Equal(StudySessionStatus.Active, sessions[1].Status);
        Assert.Equal(StudySessionTrigger.Recovery, sessions[1].Trigger);
        Assert.Equal(Now.AddMinutes(20), sessions[1].StartedAtUtc);
        await coordinator.ReconcileAfterRestartAsync(Now.AddMinutes(21));
        Assert.Equal(2, await fixture.Db.StudySessions.CountAsync());
        Assert.Equal(1, engine.ReconcileCalls);
    }

    [Fact]
    public async Task Heartbeat_counts_at_most_ten_seconds_and_ignores_clock_rollback_or_missing_session()
    {
        await using var fixture = await Fixture.Create();
        var active = StudySession.Start(null, StudySessionTrigger.Manual, true, new DateOnly(2026, 10, 26), Now);
        fixture.Db.StudySessions.Add(active);
        await fixture.Db.SaveChangesAsync();
        var coordinator = new ProtectionCoordinator(fixture.Db, new FakeProtectionEngine());

        await coordinator.HeartbeatAsync(Now.AddSeconds(8));
        Assert.Equal(8, (await fixture.Db.StudySessions.SingleAsync()).ActiveSeconds);
        await coordinator.HeartbeatAsync(Now.AddSeconds(30));
        Assert.Equal(18, (await fixture.Db.StudySessions.SingleAsync()).ActiveSeconds);
        await coordinator.HeartbeatAsync(Now.AddSeconds(20));
        Assert.Equal(18, (await fixture.Db.StudySessions.SingleAsync()).ActiveSeconds);

        var ended = await fixture.Db.StudySessions.SingleAsync();
        ended.Complete(Now.AddSeconds(31));
        await fixture.Db.SaveChangesAsync();
        await coordinator.HeartbeatAsync(Now.AddSeconds(40));
        Assert.Equal(18, (await fixture.Db.StudySessions.SingleAsync()).ActiveSeconds);
    }

    [Fact]
    public async Task Concurrent_enable_requests_create_only_one_active_session()
    {
        await using var fixture = await Fixture.Create();
        var coordinator = new ProtectionCoordinator(fixture.Db, new FakeProtectionEngine());
        var parent = AuthenticatedSession();

        await Task.WhenAll(
            coordinator.SetProtectionAsync(true, parent, Now.AddSeconds(1)),
            coordinator.SetProtectionAsync(true, parent, Now.AddSeconds(1)));

        Assert.Equal(1, await fixture.Db.StudySessions.CountAsync(x => x.Status == StudySessionStatus.Active));
    }

    [Fact]
    public async Task Failed_heartbeat_write_does_not_commit_in_memory_time_increment()
    {
        await using var fixture = await Fixture.Create();
        fixture.Db.StudySessions.Add(StudySession.Start(null, StudySessionTrigger.Manual, true, new DateOnly(2026, 10, 26), Now));
        await fixture.Db.SaveChangesAsync();
        await fixture.Db.Database.ExecuteSqlRawAsync("CREATE TRIGGER RejectSessionHeartbeat BEFORE UPDATE ON StudySessions BEGIN SELECT RAISE(ABORT, 'simulated database write failure'); END;");
        var coordinator = new ProtectionCoordinator(fixture.Db, new FakeProtectionEngine());

        await Assert.ThrowsAsync<DbUpdateException>(() => coordinator.HeartbeatAsync(Now.AddSeconds(10)));

        fixture.Db.ChangeTracker.Clear();
        Assert.Equal(0, (await fixture.Db.StudySessions.SingleAsync()).ActiveSeconds);
    }

    [Fact]
    public async Task No_engine_never_changes_persisted_protection_state()
    {
        await using var fixture = await Fixture.Create();
        var engine = new FakeProtectionEngine { IsEngineAvailable = false };
        var coordinator = new ProtectionCoordinator(fixture.Db, engine);
        await Assert.ThrowsAsync<InvalidOperationException>(() => coordinator.SetProtectionAsync(true, AuthenticatedSession(), Now.AddSeconds(1)));
        Assert.Equal(ProtectionState.Off, (await fixture.Db.AppSettings.SingleAsync()).Protection);
        Assert.Empty(await fixture.Db.StudySessions.ToArrayAsync());
    }

    [Fact]
    public async Task Protection_changes_require_an_active_parent_mode_session()
    {
        await using var fixture = await Fixture.Create();
        var engine = new FakeProtectionEngine();
        var coordinator = new ProtectionCoordinator(fixture.Db, engine);
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => coordinator.SetProtectionAsync(true, new ParentModeSession(), Now.AddSeconds(1)));
        Assert.Equal(0, engine.StartCalls);
        Assert.Equal(ProtectionState.Off, (await fixture.Db.AppSettings.SingleAsync()).Protection);
        Assert.Empty(await fixture.Db.StudySessions.ToArrayAsync());
    }

    [Fact]
    public async Task Authorized_clean_exit_stops_engine_completes_session_and_records_clean_shutdown()
    {
        await using var fixture = await Fixture.Create();
        var engine = new FakeProtectionEngine();
        var coordinator = new ProtectionCoordinator(fixture.Db, engine);
        await coordinator.SetProtectionAsync(true, AuthenticatedSession(), Now.AddSeconds(1));

        await coordinator.CleanExitAsync(Now.AddMinutes(1));

        var settings = await fixture.Db.AppSettings.SingleAsync();
        Assert.Equal(ProtectionState.Off, settings.Protection);
        Assert.Equal(Now.AddMinutes(1), settings.LastCleanShutdownAtUtc);
        Assert.False(engine.Active);
        Assert.Equal(StudySessionStatus.Completed, (await fixture.Db.StudySessions.SingleAsync()).Status);
        Assert.Contains(await fixture.Db.AuditLogs.Select(x => x.EventType).ToArrayAsync(), x => x == "ProtectionOff");
        Assert.Contains(await fixture.Db.AuditLogs.Select(x => x.EventType).ToArrayAsync(), x => x == "CleanExit");
    }

    [Fact]
    public async Task System_shutdown_interrupts_session_but_preserves_protection_for_restart()
    {
        await using var fixture = await Fixture.Create();
        var engine = new FakeProtectionEngine();
        var coordinator = new ProtectionCoordinator(fixture.Db, engine);
        await coordinator.SetProtectionAsync(true, AuthenticatedSession(), Now.AddSeconds(1));
        var heartbeat = Now.AddSeconds(30);
        (await fixture.Db.StudySessions.SingleAsync()).Heartbeat(heartbeat);
        await fixture.Db.SaveChangesAsync();

        await coordinator.HandleSystemShutdownAsync(Now.AddMinutes(1));

        var settings = await fixture.Db.AppSettings.SingleAsync();
        Assert.Equal(ProtectionState.On, settings.Protection);
        Assert.Null(settings.LastCleanShutdownAtUtc);
        Assert.Equal(StudySessionStatus.Interrupted, (await fixture.Db.StudySessions.SingleAsync()).Status);
        Assert.Equal(heartbeat, (await fixture.Db.StudySessions.SingleAsync()).EndedAtUtc);
        Assert.True(engine.Active);
        Assert.Contains(await fixture.Db.AuditLogs.Select(x => x.EventType).ToArrayAsync(), x => x == "SystemShutdown");
        Assert.Contains(await fixture.Db.AuditLogs.Select(x => x.EventType).ToArrayAsync(), x => x == "SessionInterrupted");
    }

    [Fact]
    public async Task Failed_clean_exit_restores_protection_and_never_records_clean_shutdown()
    {
        await using var fixture = await Fixture.Create();
        var engine = new FakeProtectionEngine();
        var coordinator = new ProtectionCoordinator(fixture.Db, engine);
        await coordinator.SetProtectionAsync(true, AuthenticatedSession(), Now.AddSeconds(1));
        engine.FailStop = true;

        await Assert.ThrowsAsync<InvalidOperationException>(() => coordinator.CleanExitAsync(Now.AddMinutes(1)));

        var settings = await fixture.Db.AppSettings.SingleAsync();
        Assert.Equal(ProtectionState.On, settings.Protection);
        Assert.Null(settings.LastCleanShutdownAtUtc);
        Assert.Equal(StudySessionStatus.Active, (await fixture.Db.StudySessions.SingleAsync()).Status);
        Assert.True(engine.Active);
        Assert.Equal(1, engine.ReconcileCalls);
        Assert.DoesNotContain(await fixture.Db.AuditLogs.Select(x => x.EventType).ToArrayAsync(), x => x == "CleanExit");
    }

    private static ParentModeSession AuthenticatedSession()
    {
        var session = new ParentModeSession();
        session.Unlock(Now);
        return session;
    }

    private sealed class FakeProtectionEngine : IProtectionEnginePort
    {
        public bool IsEngineAvailable { get; init; } = true;
        public bool IsAvailable => IsEngineAvailable;
        public bool Active { get; private set; }
        public bool FailStart { get; init; }
        public bool FailStop { get; set; }
        public int StartCalls { get; private set; }
        public int StopCalls { get; private set; }
        public int ReconcileCalls { get; private set; }

        public Task StartAsync(IReadOnlyCollection<BlockRule> rules, CancellationToken cancellationToken = default)
        {
            StartCalls++;
            Active = true;
            if (FailStart) throw new InvalidOperationException("simulated start failure");
            return Task.CompletedTask;
        }

        public Task StopAsync(CancellationToken cancellationToken = default)
        {
            StopCalls++;
            if (FailStop) throw new InvalidOperationException("simulated stop failure");
            Active = false;
            return Task.CompletedTask;
        }

        public Task ReconcileAsync(bool protectionEnabled, IReadOnlyCollection<BlockRule> rules, CancellationToken cancellationToken = default)
        {
            ReconcileCalls++;
            Active = protectionEnabled;
            return Task.CompletedTask;
        }
    }

    private sealed class Fixture(SqliteConnection connection, AppDbContext db) : IAsyncDisposable
    {
        public AppDbContext Db { get; } = db;

        public static async Task<Fixture> Create()
        {
            var connection = new SqliteConnection("Data Source=:memory:");
            await connection.OpenAsync();
            var options = new DbContextOptionsBuilder<AppDbContext>().UseSqlite(connection).Options;
            var db = new AppDbContext(options);
            await db.Database.MigrateAsync();
            db.AppSettings.Add(AppSettings.Create(Now));
            await db.SaveChangesAsync();
            var settings = await db.AppSettings.SingleAsync();
            settings.SetScheduleOverride(Now.AddHours(1), Now);
            await db.SaveChangesAsync();
            return new(connection, db);
        }

        public async ValueTask DisposeAsync()
        {
            await Db.DisposeAsync();
            await connection.DisposeAsync();
        }
    }
}
