using EduFocus.Domain.Entities;
using EduFocus.Domain.Enums;
using EduFocus.Ports.Outbound.Persistence;
using EduFocus.SqliteDataAccess.Persistence;
using Microsoft.EntityFrameworkCore;

namespace EduFocus.SqliteDataAccess.Repositories;

/// <summary>
/// Study session persistence (database schema §8). The unique partial index
/// UX_StudySessions_Active rejects a second Active session.
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
}
