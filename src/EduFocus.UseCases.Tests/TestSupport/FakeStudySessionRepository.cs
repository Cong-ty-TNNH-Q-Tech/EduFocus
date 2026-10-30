using EduFocus.Domain.Attention;
using EduFocus.Domain.Entities;
using EduFocus.Domain.Enums;
using EduFocus.Domain.Reports;
using EduFocus.Domain.ValueObjects;
using EduFocus.Ports.Outbound.Persistence;

namespace EduFocus.UseCases.Tests.TestSupport;

internal sealed class FakeStudySessionRepository : IStudySessionRepository
{
    public List<StudySession> Sessions { get; } = [];

    public List<HeartbeatCall> Heartbeats { get; } = [];

    public bool ThrowOnGetActive { get; set; }

    public bool ThrowOnAdd { get; set; }

    public bool ThrowOnUpdate { get; set; }

    public bool ThrowOnHeartbeat { get; set; }

    public bool ThrowOnActivity { get; set; }

    public bool ThrowOnAttention { get; set; }

    public bool ThrowOnDailySummary { get; set; }

    public bool ThrowOnRangeList { get; set; }

    public DailyStudySummary? DailySummary { get; set; }

    public List<DateOnly> DailySummaryRequests { get; } = [];

    public List<(DateOnly Start, DateOnly End)> RangeRequests { get; } = [];

    public int IncrementBlockedCalls { get; private set; }

    public List<ActivityWriteCall> ActivityWrites { get; } = [];

    public List<AttentionWriteCall> AttentionWrites { get; } = [];

    public StudySession? Active => Sessions.SingleOrDefault(session => session.IsActive);

    public Task<StudySession?> GetActiveAsync(CancellationToken cancellationToken = default)
    {
        if (ThrowOnGetActive)
            throw new InvalidOperationException("The session store is unavailable.");

        return Task.FromResult(Active);
    }

    public Task<DailyStudySummary?> GetDailySummaryAsync(
        DateOnly localDate,
        CancellationToken cancellationToken = default)
    {
        DailySummaryRequests.Add(localDate);

        if (ThrowOnDailySummary)
            throw new InvalidOperationException("The session store is unavailable.");

        return Task.FromResult(DailySummary);
    }

    public Task<IReadOnlyList<StudySession>> ListByLocalDateRangeAsync(
        DateOnly startLocalDate,
        DateOnly endLocalDate,
        CancellationToken cancellationToken = default)
    {
        RangeRequests.Add((startLocalDate, endLocalDate));

        if (ThrowOnRangeList)
            throw new InvalidOperationException("The session store is unavailable.");

        IReadOnlyList<StudySession> result = Sessions
            .Where(session => session.StartLocalDate >= startLocalDate
                && session.StartLocalDate <= endLocalDate)
            .OrderBy(session => session.StartedAtUtc)
            .ToList();

        return Task.FromResult(result);
    }

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

    public Task<bool> AddActivitySecondsAsync(
        StudySessionId sessionId,
        ActivityCategory category,
        int categorySeconds,
        int idleSeconds,
        int onlineClassSeconds,
        int monitoringEligibleSeconds,
        DateTime nowUtc,
        CancellationToken cancellationToken = default)
    {
        if (ThrowOnActivity)
            throw new InvalidOperationException("The session store is unavailable.");

        var session = Sessions.SingleOrDefault(x => x.Id == sessionId && x.IsActive);

        if (session is null)
            return Task.FromResult(false);

        var stamp = nowUtc < session.UpdatedAtUtc ? session.UpdatedAtUtc : nowUtc;
        session.AddCategorySeconds(category, categorySeconds, stamp);
        if (idleSeconds > 0)
            session.AddIdleSeconds(idleSeconds, stamp);
        if (onlineClassSeconds > 0)
            session.AddOnlineClassSeconds(onlineClassSeconds, stamp);
        if (monitoringEligibleSeconds > 0)
            session.AddMonitoringEligibleSeconds(monitoringEligibleSeconds, stamp);

        ActivityWrites.Add(new ActivityWriteCall(
            sessionId,
            category,
            categorySeconds,
            idleSeconds,
            onlineClassSeconds,
            monitoringEligibleSeconds,
            nowUtc));
        return Task.FromResult(true);
    }

    public Task<bool> AddAttentionSecondsAsync(
        StudySessionId sessionId,
        AttentionState state,
        int seconds,
        double? confidence,
        DateTime nowUtc,
        CancellationToken cancellationToken = default)
    {
        if (ThrowOnAttention)
            throw new InvalidOperationException("The session store is unavailable.");

        var session = Sessions.SingleOrDefault(x => x.Id == sessionId && x.IsActive);

        if (session is null)
            return Task.FromResult(false);

        var stamp = nowUtc < session.UpdatedAtUtc ? session.UpdatedAtUtc : nowUtc;
        var monitoredBefore = session.MonitoredSeconds -
            (state == AttentionState.CameraUnavailable ? 0 : seconds);

        session.AddAttentionSeconds(state, seconds, stamp);

        if (confidence is not null && state != AttentionState.CameraUnavailable)
        {
            var weighted = monitoredBefore <= 0 || session.ConfidenceAvg is null
                ? confidence.Value
                : (session.ConfidenceAvg.Value * monitoredBefore + confidence.Value * seconds)
                    / (monitoredBefore + seconds);

            session.SetAttentionConfidence(weighted, stamp);
        }

        AttentionWrites.Add(new AttentionWriteCall(sessionId, state, seconds, confidence, nowUtc));
        return Task.FromResult(true);
    }

    public readonly record struct HeartbeatCall(
        StudySessionId SessionId,
        DateTime HeartbeatAtUtc,
        int ActiveSeconds);

    public readonly record struct ActivityWriteCall(
        StudySessionId SessionId,
        ActivityCategory Category,
        int CategorySeconds,
        int IdleSeconds,
        int OnlineClassSeconds,
        int MonitoringEligibleSeconds,
        DateTime NowUtc);

    public readonly record struct AttentionWriteCall(
        StudySessionId SessionId,
        AttentionState State,
        int Seconds,
        double? Confidence,
        DateTime NowUtc);
}
