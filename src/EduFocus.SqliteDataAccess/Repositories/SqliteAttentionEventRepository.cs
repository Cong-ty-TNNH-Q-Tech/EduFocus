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
    private readonly DbContextOptions<EduFocusDbContext> _dbContextOptions;

    public SqliteAttentionEventRepository(EduFocusDatabaseOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        _dbContextOptions = new DbContextOptionsBuilder<EduFocusDbContext>()
            .UseSqlite(options.ConnectionString)
            .AddInterceptors(new SqlitePragmaInterceptor())
            .Options;
    }

    public async Task AddAsync(AttentionEvent attentionEvent, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(attentionEvent);

        await using var context = new EduFocusDbContext(_dbContextOptions);
        context.AttentionEvents.Add(attentionEvent);
        await context.SaveChangesAsync(cancellationToken);
    }

    public async Task UpdateAsync(AttentionEvent attentionEvent, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(attentionEvent);

        await using var context = new EduFocusDbContext(_dbContextOptions);
        context.AttentionEvents.Update(attentionEvent);
        await context.SaveChangesAsync(cancellationToken);
    }

    public async Task<AttentionEvent?> GetOpenAsync(
        StudySessionId sessionId,
        CancellationToken cancellationToken = default)
    {
        EnsureSessionId(sessionId);

        await using var context = new EduFocusDbContext(_dbContextOptions);
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

        await using var context = new EduFocusDbContext(_dbContextOptions);
        return await context.AttentionEvents
            .AsNoTracking()
            .Where(x => x.SessionId == sessionId)
            .OrderBy(x => x.StartedAtUtc)
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

        await using var context = new EduFocusDbContext(_dbContextOptions);

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
