using EduFocus.Domain.Entities;
using EduFocus.Ports.Outbound.Persistence;
using EduFocus.SqliteDataAccess.Persistence;
using Microsoft.EntityFrameworkCore;

namespace EduFocus.SqliteDataAccess.Repositories;

/// <summary>
/// SQLite persistence for the singleton <see cref="AppSettings"/> row
/// (database schema §4).
/// </summary>
public sealed class SqliteAppSettingsRepository : IAppSettingsRepository
{
    private readonly SqliteDbContextFactory _contextFactory;

    public SqliteAppSettingsRepository(SqliteDbContextFactory contextFactory)
    {
        ArgumentNullException.ThrowIfNull(contextFactory);
        _contextFactory = contextFactory;
    }

    public async Task<AppSettings?> GetAsync(CancellationToken cancellationToken = default)
    {
        await using var context = _contextFactory.CreateDbContext();
        return await context.AppSettings
            .AsNoTracking()
            .SingleOrDefaultAsync(cancellationToken);
    }

    public async Task AddAsync(AppSettings settings, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(settings);

        await using var context = _contextFactory.CreateDbContext();
        context.AppSettings.Add(settings);
        await context.SaveChangesAsync(cancellationToken);
    }

    public async Task UpdateAsync(AppSettings settings, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(settings);

        await using var context = _contextFactory.CreateDbContext();
        context.AppSettings.Update(settings);
        await context.SaveChangesAsync(cancellationToken);
    }
}
