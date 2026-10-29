using EduFocus.Domain.Entities;
using EduFocus.Domain.Enums;
using EduFocus.Domain.ValueObjects;
using EduFocus.Ports.Outbound.Persistence;
using EduFocus.SqliteDataAccess.Persistence;
using Microsoft.EntityFrameworkCore;

namespace EduFocus.SqliteDataAccess.Repositories;

/// <summary>
/// Study session persistence (database schema §8). The unique partial index
/// UX_StudySessions_Active rejects a second Active session. Heartbeat and
/// blocked-attempt writes are conditional, targeted updates guarded by the
/// Active status so they never resurrect a finished session.
/// </summary>
public sealed class SqliteStudySessionRepository : IStudySessionRepository
{
    private readonly DbContextOptions<EduFocusDbContext> _dbContextOptions;

    public SqliteStudySessionRepository(EduFocusDatabaseOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        _dbContextOptions = new DbContextOptionsBuilder<EduFocusDbContext>()
            .UseSqlite(options.ConnectionString)
            .AddInterceptors(new SqlitePragmaInterceptor())
            .Options;
    }

    public async Task<StudySession?> GetActiveAsync(CancellationToken cancellationToken = default)
    {
        await using var context = new EduFocusDbContext(_dbContextOptions);
        return await context.StudySessions
            .AsNoTracking()
            .SingleOrDefaultAsync(x => x.Status == StudySessionStatus.Active, cancellationToken);
    }

    public async Task AddAsync(StudySession session, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(session);

        await using var context = new EduFocusDbContext(_dbContextOptions);
        context.StudySessions.Add(session);
        await context.SaveChangesAsync(cancellationToken);
    }

    public async Task UpdateAsync(StudySession session, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(session);

        await using var context = new EduFocusDbContext(_dbContextOptions);
        context.StudySessions.Update(session);
        await context.SaveChangesAsync(cancellationToken);
    }

    public async Task<bool> RecordHeartbeatAsync(
        StudySessionId sessionId,
        DateTime heartbeatAtUtc,
        int activeSeconds,
        CancellationToken cancellationToken = default)
    {
        if (activeSeconds < 0)
            throw new ArgumentOutOfRangeException(nameof(activeSeconds), activeSeconds, "Active seconds cannot be negative.");
        if (heartbeatAtUtc.Kind != DateTimeKind.Utc)
            throw new ArgumentException("The heartbeat must be UTC.", nameof(heartbeatAtUtc));

        await using var context = new EduFocusDbContext(_dbContextOptions);

        var affected = await context.StudySessions
            .Where(x => x.Id == sessionId
                && x.Status == StudySessionStatus.Active
                && x.LastHeartbeatAtUtc <= heartbeatAtUtc)
            .ExecuteUpdateAsync(
                setters => setters
                    .SetProperty(x => x.LastHeartbeatAtUtc, heartbeatAtUtc)
                    .SetProperty(x => x.ActiveSeconds, x => x.ActiveSeconds + activeSeconds)
                    .SetProperty(
                        x => x.UpdatedAtUtc,
                        x => x.UpdatedAtUtc < heartbeatAtUtc ? heartbeatAtUtc : x.UpdatedAtUtc),
                cancellationToken);

        return affected > 0;
    }

    public async Task<bool> IncrementBlockedAttemptCountAsync(
        StudySessionId sessionId,
        DateTime nowUtc,
        int count = 1,
        CancellationToken cancellationToken = default)
    {
        if (count < 0)
            throw new ArgumentOutOfRangeException(nameof(count), count, "Count cannot be negative.");
        if (nowUtc.Kind != DateTimeKind.Utc)
            throw new ArgumentException("The timestamp must be UTC.", nameof(nowUtc));

        await using var context = new EduFocusDbContext(_dbContextOptions);

        var affected = await context.StudySessions
            .Where(x => x.Id == sessionId && x.Status == StudySessionStatus.Active)
            .ExecuteUpdateAsync(
                setters => setters
                    .SetProperty(x => x.BlockedAttemptCount, x => x.BlockedAttemptCount + count)
                    .SetProperty(
                        x => x.UpdatedAtUtc,
                        x => x.UpdatedAtUtc < nowUtc ? nowUtc : x.UpdatedAtUtc),
                cancellationToken);

        return affected > 0;
    }

    public async Task<bool> AddActivitySecondsAsync(
        StudySessionId sessionId,
        ActivityCategory category,
        int categorySeconds,
        int idleSeconds,
        int onlineClassSeconds,
        DateTime nowUtc,
        CancellationToken cancellationToken = default)
    {
        if (!Enum.IsDefined(category))
            throw new ArgumentOutOfRangeException(nameof(category), category, "Unknown activity category.");
        if (categorySeconds < 0)
            throw new ArgumentOutOfRangeException(nameof(categorySeconds), categorySeconds, "Seconds cannot be negative.");
        if (idleSeconds < 0)
            throw new ArgumentOutOfRangeException(nameof(idleSeconds), idleSeconds, "Seconds cannot be negative.");
        if (onlineClassSeconds < 0)
            throw new ArgumentOutOfRangeException(nameof(onlineClassSeconds), onlineClassSeconds, "Seconds cannot be negative.");
        if (nowUtc.Kind != DateTimeKind.Utc)
            throw new ArgumentException("The timestamp must be UTC.", nameof(nowUtc));

        await using var context = new EduFocusDbContext(_dbContextOptions);

        var query = context.StudySessions
            .Where(x => x.Id == sessionId && x.Status == StudySessionStatus.Active);

        var affected = category switch
        {
            ActivityCategory.Study => await query.ExecuteUpdateAsync(
                setters => setters
                    .SetProperty(x => x.StudySeconds, x => x.StudySeconds + categorySeconds)
                    .SetProperty(x => x.IdleSeconds, x => x.IdleSeconds + idleSeconds)
                    .SetProperty(x => x.OnlineClassSeconds, x => x.OnlineClassSeconds + onlineClassSeconds)
                    .SetProperty(
                        x => x.UpdatedAtUtc,
                        x => x.UpdatedAtUtc < nowUtc ? nowUtc : x.UpdatedAtUtc),
                cancellationToken),
            ActivityCategory.Distraction => await query.ExecuteUpdateAsync(
                setters => setters
                    .SetProperty(x => x.DistractionSeconds, x => x.DistractionSeconds + categorySeconds)
                    .SetProperty(x => x.IdleSeconds, x => x.IdleSeconds + idleSeconds)
                    .SetProperty(x => x.OnlineClassSeconds, x => x.OnlineClassSeconds + onlineClassSeconds)
                    .SetProperty(
                        x => x.UpdatedAtUtc,
                        x => x.UpdatedAtUtc < nowUtc ? nowUtc : x.UpdatedAtUtc),
                cancellationToken),
            _ => await query.ExecuteUpdateAsync(
                setters => setters
                    .SetProperty(x => x.NeutralSeconds, x => x.NeutralSeconds + categorySeconds)
                    .SetProperty(x => x.IdleSeconds, x => x.IdleSeconds + idleSeconds)
                    .SetProperty(x => x.OnlineClassSeconds, x => x.OnlineClassSeconds + onlineClassSeconds)
                    .SetProperty(
                        x => x.UpdatedAtUtc,
                        x => x.UpdatedAtUtc < nowUtc ? nowUtc : x.UpdatedAtUtc),
                cancellationToken)
        };

        return affected > 0;
    }
}
