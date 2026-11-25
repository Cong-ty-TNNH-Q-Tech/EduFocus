using EduFocus.Domain.Entities;

namespace EduFocus.Ports.Outbound.Persistence;

/// <summary>
/// Best-effort blocked attempt log (database schema §11). Rows are written by
/// the protection engine after its 30-second debounce; callers must not treat
/// counts as totals because hosts cannot observe HTTPS traffic.
/// </summary>
public interface IBlockedAttemptRepository
{
    Task AddAsync(BlockedAttempt attempt, CancellationToken cancellationToken = default);

    /// <summary>
    /// Returns the newest insertion timestamp for a web (domain or URL) block
    /// after <paramref name="afterUtc"/>, or null when nothing is newer. Used by
    /// the cloud producer so application blocks never raise a web alert.
    /// </summary>
    Task<DateTime?> GetLatestWebAttemptCreatedAtUtcAsync(
        DateTime afterUtc,
        CancellationToken cancellationToken = default);
}
