namespace EduFocus.Ports.Outbound.Persistence;

/// <summary>
/// Batched retention deletes (database schema §14). Every method deletes at
/// most <c>batchSize</c> rows older than <c>cutoffUtc</c> so a large cleanup
/// never holds the SQLite write lock for long. Active sessions and their
/// detailed rows are never touched.
/// </summary>
public interface IRetentionRepository
{
    Task<int> DeleteActivitySamplesBeforeAsync(
        DateTime cutoffUtc,
        int batchSize,
        CancellationToken cancellationToken = default);

    Task<int> DeleteAttentionEventsBeforeAsync(
        DateTime cutoffUtc,
        int batchSize,
        CancellationToken cancellationToken = default);

    Task<int> DeleteBlockedAttemptsBeforeAsync(
        DateTime cutoffUtc,
        int batchSize,
        CancellationToken cancellationToken = default);

    Task<int> DeleteFinishedSessionsBeforeAsync(
        DateTime cutoffUtc,
        int batchSize,
        CancellationToken cancellationToken = default);

    Task<int> DeleteAuditLogsBeforeAsync(
        DateTime cutoffUtc,
        int batchSize,
        CancellationToken cancellationToken = default);

    /// <summary>Frees pages released by the deletes; a no-op when auto-vacuum is off.</summary>
    Task VacuumAsync(CancellationToken cancellationToken = default);
}
