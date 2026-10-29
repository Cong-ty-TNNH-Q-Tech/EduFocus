using EduFocus.Domain.Attention;
using EduFocus.Domain.Entities;
using EduFocus.Domain.Enums;
using EduFocus.Domain.ValueObjects;

namespace EduFocus.Ports.Outbound.Persistence;

/// <summary>
/// Study session persistence. The database guarantees at most one Active
/// session via the unique partial index UX_StudySessions_Active
/// (database_schema §8). Heartbeats and counter increments are targeted updates
/// so they cannot overwrite each other with a stale full-entity snapshot.
/// </summary>
public interface IStudySessionRepository
{
    Task<StudySession?> GetActiveAsync(CancellationToken cancellationToken = default);

    Task AddAsync(StudySession session, CancellationToken cancellationToken = default);

    Task UpdateAsync(StudySession session, CancellationToken cancellationToken = default);

    /// <summary>
    /// Applies a heartbeat only while the session is Active and the stored
    /// heartbeat does not follow <paramref name="heartbeatAtUtc"/>. Returns false
    /// when no row matched, meaning the session already ended.
    /// </summary>
    Task<bool> RecordHeartbeatAsync(
        StudySessionId sessionId,
        DateTime heartbeatAtUtc,
        int activeSeconds,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Increments the blocked-attempt counter without rewriting the heartbeat
    /// or other counters. Returns false when the session is no longer Active.
    /// </summary>
    Task<bool> IncrementBlockedAttemptCountAsync(
        StudySessionId sessionId,
        DateTime nowUtc,
        int count = 1,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Adds foreground-activity totals to an Active session with targeted
    /// increments: category seconds, the separate idle overlay and the online
    /// class overlay. Returns false when the session already ended. No other
    /// counters, including the heartbeat, are rewritten.
    /// </summary>
    Task<bool> AddActivitySecondsAsync(
        StudySessionId sessionId,
        ActivityCategory category,
        int categorySeconds,
        int idleSeconds,
        int onlineClassSeconds,
        DateTime nowUtc,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Adds attention seconds to an Active session with a targeted increment.
    /// <paramref name="confidence"/> updates the session's duration-weighted
    /// average and must be null for CameraUnavailable, which never enters the
    /// attention denominator. Returns false when the session already ended.
    /// </summary>
    Task<bool> AddAttentionSecondsAsync(
        StudySessionId sessionId,
        AttentionState state,
        int seconds,
        double? confidence,
        DateTime nowUtc,
        CancellationToken cancellationToken = default);
}
