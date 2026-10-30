using EduFocus.Domain.Entities;
using EduFocus.Ports.Outbound.Persistence;
using EduFocus.SqliteDataAccess.Persistence;
using Microsoft.EntityFrameworkCore;

namespace EduFocus.SqliteDataAccess.Repositories;

/// <summary>
/// SQLite persistence for the singleton <see cref="ParentCredential"/> row
/// (database schema §5). Only PBKDF2 hashes and lockout state are stored.
/// </summary>
public sealed class SqliteParentCredentialRepository : IParentCredentialRepository
{
    private readonly SqliteDbContextFactory _contextFactory;

    public SqliteParentCredentialRepository(SqliteDbContextFactory contextFactory)
    {
        ArgumentNullException.ThrowIfNull(contextFactory);
        _contextFactory = contextFactory;
    }

    public async Task<ParentCredential?> GetAsync(CancellationToken cancellationToken = default)
    {
        await using var context = _contextFactory.CreateDbContext();
        return await context.ParentCredentials
            .AsNoTracking()
            .SingleOrDefaultAsync(cancellationToken);
    }

    public async Task AddAsync(ParentCredential credential, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(credential);

        await using var context = _contextFactory.CreateDbContext();
        context.ParentCredentials.Add(credential);
        await context.SaveChangesAsync(cancellationToken);
    }

    public async Task UpdateAsync(ParentCredential credential, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(credential);

        await using var context = _contextFactory.CreateDbContext();
        context.ParentCredentials.Update(credential);
        await context.SaveChangesAsync(cancellationToken);
    }
}
