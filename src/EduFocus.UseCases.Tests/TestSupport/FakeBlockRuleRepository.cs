using EduFocus.Domain.Entities;
using EduFocus.Domain.ValueObjects;
using EduFocus.Ports.Outbound.Persistence;

namespace EduFocus.UseCases.Tests.TestSupport;

internal sealed class FakeBlockRuleRepository : IBlockRuleRepository
{
    public List<BlockRule> Rules { get; } = [];

    public int UpdateCount { get; private set; }

    public Task<IReadOnlyList<BlockRule>> ListAsync(CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<BlockRule>>(Rules.ToList());

    public Task<BlockRule?> GetAsync(
        BlockRuleId id,
        CancellationToken cancellationToken = default) =>
        Task.FromResult(Rules.SingleOrDefault(rule => rule.Id == id));

    public Task AddAsync(BlockRule rule, CancellationToken cancellationToken = default)
    {
        Rules.Add(rule);
        return Task.CompletedTask;
    }

    public Task UpdateAsync(BlockRule rule, CancellationToken cancellationToken = default)
    {
        UpdateCount++;
        return Task.CompletedTask;
    }

    public Task DeleteAsync(BlockRuleId id, CancellationToken cancellationToken = default)
    {
        Rules.RemoveAll(rule => rule.Id == id);
        return Task.CompletedTask;
    }
}
