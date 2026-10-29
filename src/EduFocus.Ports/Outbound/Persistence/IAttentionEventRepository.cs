using EduFocus.Domain.Entities;
using EduFocus.Domain.ValueObjects;

namespace EduFocus.Ports.Outbound.Persistence;

/// <summary>
/// Attention event persistence (database_schema §10). Events are opened only
/// when the smoothed state changes and closed exactly once. Nothing in this
/// contract stores frames, landmarks or embeddings.
/// </summary>
public interface IAttentionEventRepository
{
    Task AddAsync(AttentionEvent attentionEvent, CancellationToken cancellationToken = default);

    Task UpdateAsync(AttentionEvent attentionEvent, CancellationToken cancellationToken = default);

    Task<AttentionEvent?> GetOpenAsync(
        StudySessionId sessionId,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<AttentionEvent>> ListBySessionAsync(
        StudySessionId sessionId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Closes every event still open for the session at the supplied instant.
    /// Crash recovery passes the session's last heartbeat so downtime is never
    /// attributed to attention. Returns the number of events closed.
    /// </summary>
    Task<int> CloseOpenAsync(
        StudySessionId sessionId,
        DateTime endedAtUtc,
        CancellationToken cancellationToken = default);
}
