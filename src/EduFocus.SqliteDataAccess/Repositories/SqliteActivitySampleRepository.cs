using EduFocus.Domain.Entities;
using EduFocus.Domain.ValueObjects;
using EduFocus.Ports.DTOs;
using EduFocus.Ports.Outbound.Persistence;
using EduFocus.SqliteDataAccess.Persistence;
using Microsoft.EntityFrameworkCore;

namespace EduFocus.SqliteDataAccess.Repositories;

/// <summary>Foreground activity sample persistence (database schema §9).</summary>
public sealed class SqliteActivitySampleRepository : IActivitySampleRepository
{
    private readonly DbContextOptions<EduFocusDbContext> _dbContextOptions;

    public SqliteActivitySampleRepository(EduFocusDatabaseOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        _dbContextOptions = new DbContextOptionsBuilder<EduFocusDbContext>()
            .UseSqlite(options.ConnectionString)
            .AddInterceptors(new SqlitePragmaInterceptor())
            .Options;
    }

    public async Task AddAsync(ActivitySample sample, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(sample);

        await using var context = new EduFocusDbContext(_dbContextOptions);
        context.ActivitySamples.Add(sample);
        await context.SaveChangesAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<ReportActivitySample>> ListForReportAsync(
        IReadOnlyCollection<StudySessionId> sessionIds,
        int maxCount,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(sessionIds);

        if (maxCount <= 0)
            throw new ArgumentOutOfRangeException(nameof(maxCount), maxCount, "The sample limit must be positive.");
        if (sessionIds.Count == 0)
            return [];

        var ids = sessionIds.Distinct().ToArray();

        await using var context = new EduFocusDbContext(_dbContextOptions);

        // IX_ActivitySamples_SessionId_SampledAtUtc serves the IN filter and
        // the newest-first ordering; only report-safe fields are projected.
        return await context.ActivitySamples
            .AsNoTracking()
            .Where(x => ids.Contains(x.SessionId) && x.DurationSeconds > x.IdleSeconds)
            .OrderByDescending(x => x.SampledAtUtc)
            .Take(maxCount)
            .Select(x => new ReportActivitySample(
                x.SessionId,
                x.SampledAtUtc,
                x.DurationSeconds - x.IdleSeconds,
                x.Category,
                x.AppName,
                x.Domain))
            .ToListAsync(cancellationToken);
    }
}
