using EduFocus.Domain.Attention;
using EduFocus.Domain.Entities;
using EduFocus.Domain.Enums;
using EduFocus.Domain.ValueObjects;
using EduFocus.SqliteDataAccess.Repositories;
using EduFocus.SqliteDataAccess.Tests.TestSupport;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;

namespace EduFocus.SqliteDataAccess.Tests;

public sealed class AttentionEventRepositoryTests
{
    private static readonly DateTime Now = new(2026, 10, 30, 8, 0, 0, DateTimeKind.Utc);

    [Fact]
    public async Task Open_event_round_trips_with_its_state_and_confidence()
    {
        await using var db = new TempDatabase();
        await db.CreateInitializer().InitializeAsync();
        var session = await SeedSessionAsync(db);

        var repository = new SqliteAttentionEventRepository(db.Options);
        var attentionEvent = AttentionEvent.Open(
            session.Id,
            AttentionState.Focused,
            Now.AddSeconds(1),
            confidence: 0.75,
            note: "frontal");

        await repository.AddAsync(attentionEvent);

        var open = await repository.GetOpenAsync(session.Id);
        open.Should().NotBeNull();
        open!.Id.Should().Be(attentionEvent.Id);
        open.SessionId.Should().Be(session.Id);
        open.State.Should().Be(AttentionState.Focused);
        open.StartedAtUtc.Should().Be(Now.AddSeconds(1));
        open.EndedAtUtc.Should().BeNull();
        open.DurationSeconds.Should().Be(0);
        open.Confidence.Should().BeApproximately(0.75, 0.0001);
        open.Note.Should().Be("frontal");
        open.IsOpen.Should().BeTrue();
    }

    [Fact]
    public async Task Closed_event_is_no_longer_returned_as_open()
    {
        await using var db = new TempDatabase();
        await db.CreateInitializer().InitializeAsync();
        var session = await SeedSessionAsync(db);

        var repository = new SqliteAttentionEventRepository(db.Options);
        var attentionEvent = AttentionEvent.Open(
            session.Id,
            AttentionState.Distracted,
            Now.AddSeconds(1));
        await repository.AddAsync(attentionEvent);

        attentionEvent.Close(Now.AddSeconds(21));
        await repository.UpdateAsync(attentionEvent);

        (await repository.GetOpenAsync(session.Id)).Should().BeNull();

        var events = await repository.ListBySessionAsync(session.Id);
        events.Should().ContainSingle();
        events[0].EndedAtUtc.Should().Be(Now.AddSeconds(21));
        events[0].DurationSeconds.Should().Be(20);
        events[0].IsOpen.Should().BeFalse();
    }

    [Fact]
    public async Task List_by_session_returns_events_in_start_order()
    {
        await using var db = new TempDatabase();
        await db.CreateInitializer().InitializeAsync();
        var session = await SeedSessionAsync(db);

        var repository = new SqliteAttentionEventRepository(db.Options);
        var later = AttentionEvent.Open(session.Id, AttentionState.Absent, Now.AddSeconds(30));
        var earlier = AttentionEvent.Open(session.Id, AttentionState.Focused, Now.AddSeconds(1));

        await repository.AddAsync(later);
        await repository.AddAsync(earlier);

        var events = await repository.ListBySessionAsync(session.Id);

        events.Should().HaveCount(2);
        events[0].State.Should().Be(AttentionState.Focused);
        events[1].State.Should().Be(AttentionState.Absent);
    }

    [Fact]
    public async Task Close_open_closes_only_the_requested_session_at_the_given_time()
    {
        await using var db = new TempDatabase();
        await db.CreateInitializer().InitializeAsync();
        var session = await SeedSessionAsync(db, active: false);
        var other = await SeedSessionAsync(db, Now.AddMinutes(5));

        var repository = new SqliteAttentionEventRepository(db.Options);
        var first = AttentionEvent.Open(session.Id, AttentionState.Focused, Now.AddSeconds(1));
        var second = AttentionEvent.Open(session.Id, AttentionState.CameraUnavailable, Now.AddSeconds(5));
        var foreign = AttentionEvent.Open(other.Id, AttentionState.Focused, Now.AddSeconds(1));
        await repository.AddAsync(first);
        await repository.AddAsync(second);
        await repository.AddAsync(foreign);

        var closed = await repository.CloseOpenAsync(session.Id, Now.AddSeconds(10));

        closed.Should().Be(2);
        var events = await repository.ListBySessionAsync(session.Id);
        events.Should().OnlyContain(x => x.EndedAtUtc == Now.AddSeconds(10));
        events[0].DurationSeconds.Should().Be(9);
        events[1].DurationSeconds.Should().Be(5);

        var foreignOpen = await repository.GetOpenAsync(other.Id);
        foreignOpen.Should().NotBeNull();
        foreignOpen!.EndedAtUtc.Should().BeNull();
    }

    [Fact]
    public async Task Close_open_clamps_the_end_to_the_event_start()
    {
        await using var db = new TempDatabase();
        await db.CreateInitializer().InitializeAsync();
        var session = await SeedSessionAsync(db);

        var repository = new SqliteAttentionEventRepository(db.Options);
        var attentionEvent = AttentionEvent.Open(session.Id, AttentionState.Focused, Now.AddSeconds(10));
        await repository.AddAsync(attentionEvent);

        var closed = await repository.CloseOpenAsync(session.Id, Now.AddSeconds(1));

        closed.Should().Be(1);
        var events = await repository.ListBySessionAsync(session.Id);
        events[0].EndedAtUtc.Should().Be(Now.AddSeconds(10));
        events[0].DurationSeconds.Should().Be(0);
    }

    [Fact]
    public async Task Close_open_without_open_events_returns_zero()
    {
        await using var db = new TempDatabase();
        await db.CreateInitializer().InitializeAsync();
        var session = await SeedSessionAsync(db);

        var repository = new SqliteAttentionEventRepository(db.Options);

        (await repository.CloseOpenAsync(session.Id, Now.AddSeconds(10))).Should().Be(0);
    }

    [Fact]
    public async Task Repository_rejects_missing_sessions_and_local_timestamps()
    {
        await using var db = new TempDatabase();
        await db.CreateInitializer().InitializeAsync();

        var repository = new SqliteAttentionEventRepository(db.Options);

        var missingId = () => repository.GetOpenAsync(default);
        await missingId.Should().ThrowAsync<ArgumentException>();

        var localTime = () => repository.CloseOpenAsync(
            new StudySessionId(Guid.NewGuid()),
            DateTime.SpecifyKind(Now, DateTimeKind.Unspecified));
        await localTime.Should().ThrowAsync<ArgumentException>();
    }

    private static async Task<StudySession> SeedSessionAsync(
        TempDatabase db,
        DateTime? startedAt = null,
        bool active = true)
    {
        var start = startedAt ?? Now;
        var session = StudySession.Start(
            start,
            new DateOnly(2026, 10, 30),
            SessionTrigger.Manual,
            monitoringEnabled: true);

        if (!active)
            session.Abort(start.AddMinutes(1));

        await using var context = db.CreateContext();
        context.StudySessions.Add(session);
        await context.SaveChangesAsync();
        return session;
    }
}
