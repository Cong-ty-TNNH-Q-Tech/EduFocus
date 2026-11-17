using EduFocus.Domain.Entities;

namespace EduFocus.Ports.Outbound.Persistence;

/// <summary>Persists the singleton <see cref="CloudLink"/> row.</summary>
public interface ICloudLinkRepository
{
    Task<CloudLink?> GetAsync(CancellationToken cancellationToken = default);

    Task AddAsync(CloudLink link, CancellationToken cancellationToken = default);

    Task UpdateAsync(CloudLink link, CancellationToken cancellationToken = default);

    /// <summary>
    /// Records a successful sync without overwriting a concurrent unlink or
    /// re-pair. Returns false when the link is disabled or the token changed.
    /// </summary>
    Task<bool> TryRecordSyncAsync(
        string desktopToken,
        DateTime lastSyncAtUtc,
        DateTime updatedAtUtc,
        CancellationToken cancellationToken = default);

    Task<bool> TryRecordErrorAsync(
        string desktopToken,
        string? message,
        DateTime updatedAtUtc,
        CancellationToken cancellationToken = default);
}
