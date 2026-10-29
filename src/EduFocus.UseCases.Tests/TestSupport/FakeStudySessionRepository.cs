using EduFocus.Domain.Entities;
using EduFocus.Domain.ValueObjects;
using EduFocus.Ports.Outbound.Persistence;

namespace EduFocus.UseCases.Tests.TestSupport;

internal sealed class FakeStudySessionRepository : IStudySessionRepository
{
    public List<StudySession> Sessions { get; } = [];

    public List<HeartbeatCall> Heartbeats { get; } = [];

    public bool ThrowOnAdd { get; set; }

    public bool ThrowOnUpdate { get; set; }

    public bool ThrowOnHeartbeat { get; set; }

    public int IncrementBlockedCalls { get; private set; }

    public StudySession? Active => Sessions.SingleOrDefault(session => session.IsActive);

    public Task<StudySession?> GetActiveAsync(CancellationToken cancellationToken = default) =>
        Task.FromResult(Active);

    public Task AddAsync(StudySession session, CancellationToken cancellationToken = default)
    {
        if (ThrowOnAdd)
            throw new InvalidOperationException("The session store is unavailable.");

        if (session.IsActive && Active is not null)
            throw new InvalidOperationException("An active session already exists.");

        Sessions.Add(session);
        return Task.CompletedTask;
    }

    public Task UpdateAsync(StudySession session, CancellationToken cancellationToken = default)
    {
        if (ThrowOnUpdate)
            throw new InvalidOperationException("The session store is unavailable.");

        if (!Sessions.Contains(session))
            Sessions.Add(session);

        return Task.CompletedTask;
    }

    public Task<bool> RecordHeartbeatAsync(
        StudySessionId sessionId,
        DateTime heartbeatAtUtc,
        int activeSeconds,
        CancellationToken cancellationToken = default)
    {
        if (ThrowOnHeartbeat)
            throw new InvalidOperationException("The session store is unavailable.");

        var session = Sessions.SingleOrDefault(x => x.Id == sessionId && x.IsActive);

        if (session is null || heartbeatAtUtc < session.LastHeartbeatAtUtc)
            return Task.FromResult(false);

        // The real repository updates the row directly; clamp only to satisfy
        // the domain's monotonic UpdatedAt guard in this in-memory fake.
        var beat = heartbeatAtUtc < session.UpdatedAtUtc ? session.UpdatedAtUtc : heartbeatAtUtc;
        session.Heartbeat(beat);
        session.AddActiveSeconds(activeSeconds, beat);

        Heartbeats.Add(new HeartbeatCall(sessionId, heartbeatAtUtc, activeSeconds));
        return Task.FromResult(true);
    }

    public Task<bool> IncrementBlockedAttemptCountAsync(
        StudySessionId sessionId,
        DateTime nowUtc,
        int count = 1,
        CancellationToken cancellationToken = default)
    {
        IncrementBlockedCalls++;

        var session = Sessions.SingleOrDefault(x => x.Id == sessionId && x.IsActive);

        if (session is null)
            return Task.FromResult(false);

        var stamp = nowUtc < session.UpdatedAtUtc ? session.UpdatedAtUtc : nowUtc;
        session.IncrementBlockedAttempts(stamp, count);
        return Task.FromResult(true);
    }

    public readonly record struct HeartbeatCall(
        StudySessionId SessionId,
        DateTime HeartbeatAtUtc,
        int ActiveSeconds);
}
