using EduFocus.Ports.DTOs;

namespace EduFocus.Ports.Outbound.Persistence;

/// <summary>
/// Storage inspection and batched delete-all for the privacy screen (UC11,
/// issue #38). Deletes never touch Active sessions, credentials, settings or
/// audit rows. Batches keep the SQLite write lock short.
/// </summary>
public interface IPrivacyDataRepository
{
    Task<PrivacyDataCounts> GetCountsAsync(CancellationToken cancellationToken = default);

    Task<long> GetDatabaseSizeBytesAsync(CancellationToken cancellationToken = default);

    Task<int> DeleteActivitySamplesBatchAsync(
        int batchSize,
        CancellationToken cancellationToken = default);

    Task<int> DeleteAttentionEventsBatchAsync(
        int batchSize,
        CancellationToken cancellationToken = default);

    Task<int> DeleteBlockedAttemptsBatchAsync(
        int batchSize,
        CancellationToken cancellationToken = default);

    Task<int> DeleteFinishedSessionsBatchAsync(
        int batchSize,
        CancellationToken cancellationToken = default);

    /// <summary>Reclaims free pages after a delete-all; failures are non-fatal.</summary>
    Task VacuumAsync(CancellationToken cancellationToken = default);
}
