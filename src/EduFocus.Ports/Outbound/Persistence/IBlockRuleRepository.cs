using EduFocus.Domain.Entities;
using EduFocus.Domain.ValueObjects;

namespace EduFocus.Ports.Outbound.Persistence;

/// <summary>
/// Block and allow rules (database schema §6). Matching belongs to the domain;
/// this port only persists whole rules.
/// </summary>
public interface IBlockRuleRepository
{
    Task<IReadOnlyList<BlockRule>> ListAsync(CancellationToken cancellationToken = default);

    Task<BlockRule?> GetAsync(BlockRuleId id, CancellationToken cancellationToken = default);

    Task AddAsync(BlockRule rule, CancellationToken cancellationToken = default);

    Task UpdateAsync(BlockRule rule, CancellationToken cancellationToken = default);

    Task DeleteAsync(BlockRuleId id, CancellationToken cancellationToken = default);
}
