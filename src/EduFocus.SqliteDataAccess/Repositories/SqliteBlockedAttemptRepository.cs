using EduFocus.Domain.Entities;
using EduFocus.Ports.Outbound.Persistence;
using EduFocus.SqliteDataAccess.Persistence;
using Microsoft.EntityFrameworkCore;

namespace EduFocus.SqliteDataAccess.Repositories;

/// <summary>Blocked attempt persistence (database schema §11).</summary>
public sealed class SqliteBlockedAttemptRepository : IBlockedAttemptRepository
{
    private readonly SqliteDbContextFactory _contextFactory;

    public SqliteBlockedAttemptRepository(SqliteDbContextFactory contextFactory)
    {
        ArgumentNullException.ThrowIfNull(contextFactory);
        _contextFactory = contextFactory;
    }

    public async Task AddAsync(BlockedAttempt attempt, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(attempt);

        await using var context = _contextFactory.CreateDbContext();
        context.BlockedAttempts.Add(attempt);
        await context.SaveChangesAsync(cancellationToken);
    }
}
