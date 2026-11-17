using EduFocus.Domain.Entities;
using EduFocus.Ports.DTOs;

namespace EduFocus.Ports.Outbound.Persistence;

/// <summary>
/// Durable offline queue for cloud sync events. Events keep their client event
/// id across retries so the relay can deduplicate them.
/// </summary>
public interface ICloudOutboxRepository
{
    Task AddAsync(CloudOutboxEvent item, CancellationToken cancellationToken = default);

    Task<bool> ExistsAsync(string clientEventId, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<CloudOutboxEvent>> GetDueAsync(
        DateTime utcNow,
        int limit,
        CancellationToken cancellationToken = default);

    Task MarkSentAsync(
        IReadOnlyList<Guid> ids,
        DateTime sentAtUtc,
        CancellationToken cancellationToken = default);

    Task ScheduleRetryAsync(
        IReadOnlyList<Guid> ids,
        DateTime nextAttemptAtUtc,
        string? lastError,
        CancellationToken cancellationToken = default);

    Task MarkFailedAsync(
        IReadOnlyList<Guid> ids,
        DateTime failedAtUtc,
        string? reason,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Deletes sent/rejected rows and, when the queue exceeds its limits, the
    /// oldest pending rows. Returns the number of rows removed.
    /// </summary>
    Task<int> TrimAsync(
        DateTime utcNow,
        int maxPendingEvents,
        TimeSpan maxAge,
        CancellationToken cancellationToken = default);

    Task<CloudOutboxStatus> GetStatusAsync(CancellationToken cancellationToken = default);
}
