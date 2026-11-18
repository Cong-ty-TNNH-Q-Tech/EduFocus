using EduFocus.Domain.Entities;
using EduFocus.Ports.DTOs;
using EduFocus.Ports.Outbound.Persistence;
using EduFocus.SqliteDataAccess.Persistence;
using Microsoft.EntityFrameworkCore;

namespace EduFocus.SqliteDataAccess.Repositories;

/// <summary>
/// SQLite persistence for the durable cloud outbox. Rows are encrypted at rest
/// by SQLCipher; payloads contain aggregate data only.
/// </summary>
public sealed class SqliteCloudOutboxRepository : ICloudOutboxRepository
{
    private readonly SqliteDbContextFactory _contextFactory;

    public SqliteCloudOutboxRepository(SqliteDbContextFactory contextFactory)
    {
        ArgumentNullException.ThrowIfNull(contextFactory);
        _contextFactory = contextFactory;
    }

    public async Task AddAsync(CloudOutboxEvent item, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(item);

        await using var context = _contextFactory.CreateDbContext();
        context.CloudOutbox.Add(item);
        await context.SaveChangesAsync(cancellationToken);
    }

    public async Task<bool> ExistsAsync(
        string clientEventId,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(clientEventId);

        await using var context = _contextFactory.CreateDbContext();
        return await context.CloudOutbox
            .AsNoTracking()
            .AnyAsync(e => e.ClientEventId == clientEventId, cancellationToken);
    }

    public async Task<IReadOnlyList<CloudOutboxEvent>> GetDueAsync(
        DateTime utcNow,
        int limit,
        CancellationToken cancellationToken = default)
    {
        if (limit <= 0)
            return [];

        await using var context = _contextFactory.CreateDbContext();
        return await context.CloudOutbox
            .AsNoTracking()
            .Where(e => e.SentAtUtc == null
                && e.FailedAtUtc == null
                && e.NextAttemptAtUtc <= utcNow)
            .OrderBy(e => e.CreatedAtUtc)
            .ThenBy(e => e.Id)
            .Take(limit)
            .ToListAsync(cancellationToken);
    }

    public Task MarkSentAsync(
        IReadOnlyList<Guid> ids,
        DateTime sentAtUtc,
        CancellationToken cancellationToken = default) =>
        UpdateEachAsync(ids, item => item.MarkSent(sentAtUtc), cancellationToken);

    public Task ScheduleRetryAsync(
        IReadOnlyList<Guid> ids,
        DateTime nextAttemptAtUtc,
        string? lastError,
        CancellationToken cancellationToken = default) =>
        UpdateEachAsync(ids, item => item.ScheduleRetry(nextAttemptAtUtc, lastError), cancellationToken);

    public Task MarkFailedAsync(
        IReadOnlyList<Guid> ids,
        DateTime failedAtUtc,
        string? reason,
        CancellationToken cancellationToken = default) =>
        UpdateEachAsync(ids, item => item.MarkFailed(failedAtUtc, reason), cancellationToken);

    public async Task<int> TrimAsync(
        DateTime utcNow,
        int maxPendingEvents,
        TimeSpan maxAge,
        CancellationToken cancellationToken = default)
    {
        var cutoff = utcNow - maxAge;

        await using var context = _contextFactory.CreateDbContext();

        var removed = await context.CloudOutbox
            .Where(e => e.CreatedAtUtc < cutoff)
            .ExecuteDeleteAsync(cancellationToken);

        var pendingQuery = context.CloudOutbox
            .Where(e => e.SentAtUtc == null && e.FailedAtUtc == null);
        var excess = await pendingQuery.CountAsync(cancellationToken) - maxPendingEvents;
        if (excess > 0)
        {
            var oldestIds = await pendingQuery
                .OrderBy(e => e.CreatedAtUtc)
                .ThenBy(e => e.Id)
                .Take(excess)
                .Select(e => e.Id)
                .ToListAsync(cancellationToken);

            removed += await context.CloudOutbox
                .Where(e => oldestIds.Contains(e.Id))
                .ExecuteDeleteAsync(cancellationToken);
        }

        return removed;
    }

    public async Task<int> ClearAllAsync(CancellationToken cancellationToken = default)
    {
        await using var context = _contextFactory.CreateDbContext();
        return await context.CloudOutbox.ExecuteDeleteAsync(cancellationToken);
    }

    public async Task<CloudOutboxStatus> GetStatusAsync(CancellationToken cancellationToken = default)
    {
        await using var context = _contextFactory.CreateDbContext();

        var pending = await context.CloudOutbox.CountAsync(
            e => e.SentAtUtc == null && e.FailedAtUtc == null,
            cancellationToken);
        var failed = await context.CloudOutbox.CountAsync(
            e => e.FailedAtUtc != null,
            cancellationToken);
        var lastError = await context.CloudOutbox
            .AsNoTracking()
            .Where(e => e.LastError != null)
            .OrderByDescending(e => e.CreatedAtUtc)
            .Select(e => e.LastError)
            .FirstOrDefaultAsync(cancellationToken);
        var lastSentAtUtc = await context.CloudOutbox
            .AsNoTracking()
            .Where(e => e.SentAtUtc != null)
            .OrderByDescending(e => e.SentAtUtc)
            .Select(e => e.SentAtUtc)
            .FirstOrDefaultAsync(cancellationToken);

        return new CloudOutboxStatus(pending, failed, lastError, lastSentAtUtc);
    }

    private async Task UpdateEachAsync(
        IReadOnlyList<Guid> ids,
        Action<CloudOutboxEvent> update,
        CancellationToken cancellationToken)
    {
        if (ids.Count == 0)
            return;

        await using var context = _contextFactory.CreateDbContext();
        var items = await context.CloudOutbox
            .Where(e => ids.Contains(e.Id))
            .ToListAsync(cancellationToken);

        foreach (var item in items)
            update(item);

        await context.SaveChangesAsync(cancellationToken);
    }
}
