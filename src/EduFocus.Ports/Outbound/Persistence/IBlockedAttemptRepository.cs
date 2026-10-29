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
}
