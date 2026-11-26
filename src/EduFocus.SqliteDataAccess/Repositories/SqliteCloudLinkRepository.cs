using EduFocus.Domain.Entities;
using EduFocus.Domain.Privacy;
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
        await using var transaction = await context.Database.BeginTransactionAsync(cancellationToken);
        if (await context.CloudLinks.AnyAsync(cancellationToken))
            throw new InvalidOperationException("Cloud link row already exists.");

        context.CloudLinks.Add(link);
        await context.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
    }

    public async Task UpdateAsync(CloudLink link, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(link);

        await using var context = _contextFactory.CreateDbContext();
        context.CloudLinks.Update(link);
        await context.SaveChangesAsync(cancellationToken);
    }

    public async Task<bool> TryRecordSyncAsync(
        string desktopToken,
        DateTime lastSyncAtUtc,
        DateTime updatedAtUtc,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(desktopToken);

        await using var context = _contextFactory.CreateDbContext();
        var updated = await context.CloudLinks
            .Where(link => link.IsEnabled
                && link.DesktopToken == desktopToken
                && link.UpdatedAtUtc <= updatedAtUtc)
            .ExecuteUpdateAsync(
                setters => setters
                    .SetProperty(link => link.LastSyncAtUtc, lastSyncAtUtc)
                    .SetProperty(link => link.LastError, (string?)null)
                    .SetProperty(link => link.UpdatedAtUtc, updatedAtUtc),
                cancellationToken);

        return updated > 0;
    }

    public async Task<bool> TryRecordErrorAsync(
        string desktopToken,
        string? message,
        DateTime updatedAtUtc,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(desktopToken);

        var clean = TextSanitizer.Clean(message, CloudLink.MaxErrorLength);
        await using var context = _contextFactory.CreateDbContext();
        var updated = await context.CloudLinks
            .Where(link => link.IsEnabled
                && link.DesktopToken == desktopToken
                && link.UpdatedAtUtc <= updatedAtUtc)
            .ExecuteUpdateAsync(
                setters => setters
                    .SetProperty(link => link.LastError, clean)
                    .SetProperty(link => link.UpdatedAtUtc, updatedAtUtc),
                cancellationToken);

        return updated > 0;
    }
}
