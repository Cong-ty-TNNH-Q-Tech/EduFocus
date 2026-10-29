using EduFocus.Domain.Entities;
using EduFocus.Domain.Enums;
using EduFocus.SqliteDataAccess.Repositories;
using EduFocus.SqliteDataAccess.Tests.TestSupport;
using FluentAssertions;

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

    private static async Task<StudySession> ReadSessionAsync(TempDatabase db, Guid id)
    {
        await using var context = db.CreateContext();
        var session = await context.StudySessions.FindAsync(
            new EduFocus.Domain.ValueObjects.StudySessionId(id));
        return session ?? throw new InvalidOperationException("Session was not found.");
    }
}
