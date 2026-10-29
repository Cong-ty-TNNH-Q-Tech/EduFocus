using EduFocus.Domain.Entities;
using EduFocus.Ports.Outbound.Persistence;
using EduFocus.SqliteDataAccess.Persistence;
using Microsoft.EntityFrameworkCore;

namespace EduFocus.SqliteDataAccess.Repositories;

/// <summary>Blocked attempt persistence (database schema §11).</summary>
public sealed class SqliteBlockedAttemptRepository : IBlockedAttemptRepository
{
    private readonly DbContextOptions<EduFocusDbContext> _dbContextOptions;

    public SqliteBlockedAttemptRepository(EduFocusDatabaseOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        _dbContextOptions = new DbContextOptionsBuilder<EduFocusDbContext>()
            .UseSqlite(options.ConnectionString)
            .AddInterceptors(new SqlitePragmaInterceptor())
            .Options;
    }

    public async Task AddAsync(BlockedAttempt attempt, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(attempt);

        await using var context = new EduFocusDbContext(_dbContextOptions);
        context.BlockedAttempts.Add(attempt);
        await context.SaveChangesAsync(cancellationToken);
    }
}
