using EduFocus.Domain.Entities;
using EduFocus.Domain.ValueObjects;
using EduFocus.Ports.Outbound.Persistence;
using EduFocus.SqliteDataAccess.Persistence;
using Microsoft.EntityFrameworkCore;

namespace EduFocus.SqliteDataAccess.Repositories;

/// <summary>
/// Attention event persistence (database schema §10). Events are written only
/// on state changes; this repository never stores frame, landmark or image
/// data because the entity has no such fields.
/// </summary>
public sealed class SqliteAttentionEventRepository : IAttentionEventRepository
{
    private readonly SqliteDbContextFactory _contextFactory;

    public SqliteAttentionEventRepository(SqliteDbContextFactory contextFactory)
    {
        ArgumentNullException.ThrowIfNull(contextFactory);
        _contextFactory = contextFactory;
    }

    public async Task AddAsync(AttentionEvent attentionEvent, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(attentionEvent);

        await using var context = _contextFactory.CreateDbContext();
        context.AttentionEvents.Add(attentionEvent);
        await context.SaveChangesAsync(cancellationToken);
    }

    public async Task UpdateAsync(AttentionEvent attentionEvent, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(attentionEvent);

        await using var context = _contextFactory.CreateDbContext();
        context.AttentionEvents.Update(attentionEvent);
        await context.SaveChangesAsync(cancellationToken);
    }

    public async Task<AttentionEvent?> GetOpenAsync(
        StudySessionId sessionId,
        CancellationToken cancellationToken = default)
    {
        EnsureSessionId(sessionId);

        await using var context = _contextFactory.CreateDbContext();
        return await context.AttentionEvents
            .AsNoTracking()
            .Where(x => x.SessionId == sessionId && x.EndedAtUtc == null)
            .OrderByDescending(x => x.StartedAtUtc)
            .FirstOrDefaultAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<AttentionEvent>> ListBySessionAsync(
        StudySessionId sessionId,
        CancellationToken cancellationToken = default)
    {
        EnsureSessionId(sessionId);

        await using var context = _contextFactory.CreateDbContext();
        return await context.AttentionEvents
            .AsNoTracking()
            .Where(x => x.SessionId == sessionId)
            .OrderBy(x => x.StartedAtUtc)
            .ToListAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<AttentionEvent>> ListForReportAsync(
        IReadOnlyCollection<StudySessionId> sessionIds,
        int maxCount,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(sessionIds);

        if (maxCount <= 0)
            throw new ArgumentOutOfRangeException(nameof(maxCount), maxCount, "The event limit must be positive.");
        if (sessionIds.Count == 0)
            return [];

        var ids = sessionIds.Distinct().ToArray();

        await using var context = _contextFactory.CreateDbContext();
        return await context.AttentionEvents
            .AsNoTracking()
            .Where(x => ids.Contains(x.SessionId))
            .OrderByDescending(x => x.StartedAtUtc)
            .Take(maxCount)
            .ToListAsync(cancellationToken);
    }

    public async Task<int> CloseOpenAsync(
        StudySessionId sessionId,
        DateTime endedAtUtc,
        CancellationToken cancellationToken = default)
    {
        EnsureSessionId(sessionId);

        if (endedAtUtc.Kind != DateTimeKind.Utc)
            throw new ArgumentException("The end timestamp must be UTC.", nameof(endedAtUtc));

        await using var context = _contextFactory.CreateDbContext();

        var open = await context.AttentionEvents
            .Where(x => x.SessionId == sessionId && x.EndedAtUtc == null)
            .ToListAsync(cancellationToken);

        if (open.Count == 0)
            return 0;

        foreach (var attentionEvent in open)
        {
            var closeAt = endedAtUtc < attentionEvent.StartedAtUtc
                ? attentionEvent.StartedAtUtc
                : endedAtUtc;

            attentionEvent.Close(closeAt);
        }

        await context.SaveChangesAsync(cancellationToken);
        return open.Count;
    }

    private static void EnsureSessionId(StudySessionId sessionId)
    {
        if (sessionId.IsEmpty)
            throw new ArgumentException("A session id is required.", nameof(sessionId));
    }
}
