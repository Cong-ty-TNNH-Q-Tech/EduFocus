using EduFocus.Domain.Entities;
using EduFocus.Domain.ValueObjects;
using EduFocus.Ports.Outbound.Persistence;
using EduFocus.SqliteDataAccess.Persistence;
using Microsoft.EntityFrameworkCore;

namespace EduFocus.SqliteDataAccess.Repositories;

/// <summary>Block and allow rules (database schema §6).</summary>
public sealed class SqliteBlockRuleRepository : IBlockRuleRepository
{
    private readonly SqliteDbContextFactory _contextFactory;

    public SqliteBlockRuleRepository(SqliteDbContextFactory contextFactory)
    {
        ArgumentNullException.ThrowIfNull(contextFactory);
        _contextFactory = contextFactory;
    }

    public async Task<IReadOnlyList<BlockRule>> ListAsync(CancellationToken cancellationToken = default)
    {
        await using var context = _contextFactory.CreateDbContext();
        return await context.BlockRules
            .AsNoTracking()
            .OrderBy(x => x.RuleType)
            .ThenBy(x => x.Effect)
            .ThenBy(x => x.NormalizedValue)
            .ToListAsync(cancellationToken);
    }

    public async Task<BlockRule?> GetAsync(
        BlockRuleId id,
        CancellationToken cancellationToken = default)
    {
        await using var context = _contextFactory.CreateDbContext();
        return await context.BlockRules
            .AsNoTracking()
            .SingleOrDefaultAsync(x => x.Id == id, cancellationToken);
    }

    public async Task AddAsync(BlockRule rule, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(rule);

        await using var context = _contextFactory.CreateDbContext();
        context.BlockRules.Add(rule);
        await context.SaveChangesAsync(cancellationToken);
    }

    public async Task UpdateAsync(BlockRule rule, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(rule);

        await using var context = _contextFactory.CreateDbContext();
        context.BlockRules.Update(rule);
        await context.SaveChangesAsync(cancellationToken);
    }

    public async Task DeleteAsync(BlockRuleId id, CancellationToken cancellationToken = default)
    {
        await using var context = _contextFactory.CreateDbContext();
        var rule = await context.BlockRules.SingleOrDefaultAsync(x => x.Id == id, cancellationToken);
        if (rule is null)
            return;

        context.BlockRules.Remove(rule);
        await context.SaveChangesAsync(cancellationToken);
    }
}
