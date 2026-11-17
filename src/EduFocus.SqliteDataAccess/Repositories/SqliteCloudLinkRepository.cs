using EduFocus.Domain.Entities;
using EduFocus.Ports.Outbound.Persistence;
using EduFocus.SqliteDataAccess.Persistence;
using Microsoft.EntityFrameworkCore;

namespace EduFocus.SqliteDataAccess.Repositories;

/// <summary>
/// SQLite persistence for the singleton <see cref="CloudLink"/> row. The
/// bearer token is protected by the SQLCipher file encryption of the database.
/// </summary>
public sealed class SqliteCloudLinkRepository : ICloudLinkRepository
{
    private readonly SqliteDbContextFactory _contextFactory;

    public SqliteCloudLinkRepository(SqliteDbContextFactory contextFactory)
    {
        ArgumentNullException.ThrowIfNull(contextFactory);
        _contextFactory = contextFactory;
    }

    public async Task<CloudLink?> GetAsync(CancellationToken cancellationToken = default)
    {
        await using var context = _contextFactory.CreateDbContext();
        return await context.CloudLinks
            .AsNoTracking()
            .SingleOrDefaultAsync(cancellationToken);
    }

    public async Task AddAsync(CloudLink link, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(link);

        await using var context = _contextFactory.CreateDbContext();
        context.CloudLinks.Add(link);
        await context.SaveChangesAsync(cancellationToken);
    }

    public async Task UpdateAsync(CloudLink link, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(link);

        await using var context = _contextFactory.CreateDbContext();
        context.CloudLinks.Update(link);
        await context.SaveChangesAsync(cancellationToken);
    }
}
