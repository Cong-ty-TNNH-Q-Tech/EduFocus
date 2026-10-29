using EduFocus.Domain.Entities;
using EduFocus.Domain.ValueObjects;
using EduFocus.Ports.Outbound.Persistence;
using EduFocus.SqliteDataAccess.Persistence;
using Microsoft.EntityFrameworkCore;

namespace EduFocus.SqliteDataAccess.Repositories;

/// <summary>Weekly protection windows (database schema §7).</summary>
public sealed class SqliteScheduleWindowRepository : IScheduleWindowRepository
{
    private readonly DbContextOptions<EduFocusDbContext> _dbContextOptions;

    public SqliteScheduleWindowRepository(EduFocusDatabaseOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        _dbContextOptions = new DbContextOptionsBuilder<EduFocusDbContext>()
            .UseSqlite(options.ConnectionString)
            .AddInterceptors(new SqlitePragmaInterceptor())
            .Options;
    }

    public async Task<IReadOnlyList<ScheduleWindow>> ListAsync(CancellationToken cancellationToken = default)
    {
        await using var context = new EduFocusDbContext(_dbContextOptions);
        return await context.ScheduleWindows
            .AsNoTracking()
            .OrderBy(x => x.StartMinutes)
            .ThenBy(x => x.EndMinutes)
            .ThenBy(x => x.Days)
            .ToListAsync(cancellationToken);
    }

    public async Task<ScheduleWindow?> GetAsync(
        ScheduleWindowId id,
        CancellationToken cancellationToken = default)
    {
        await using var context = new EduFocusDbContext(_dbContextOptions);
        return await context.ScheduleWindows
            .AsNoTracking()
            .SingleOrDefaultAsync(x => x.Id == id, cancellationToken);
    }

    public async Task AddAsync(ScheduleWindow window, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(window);

        await using var context = new EduFocusDbContext(_dbContextOptions);
        context.ScheduleWindows.Add(window);
        await context.SaveChangesAsync(cancellationToken);
    }

    public async Task UpdateAsync(ScheduleWindow window, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(window);

        await using var context = new EduFocusDbContext(_dbContextOptions);
        context.ScheduleWindows.Update(window);
        await context.SaveChangesAsync(cancellationToken);
    }

    public async Task DeleteAsync(ScheduleWindowId id, CancellationToken cancellationToken = default)
    {
        await using var context = new EduFocusDbContext(_dbContextOptions);
        var window = await context.ScheduleWindows.SingleOrDefaultAsync(x => x.Id == id, cancellationToken);
        if (window is null)
            return;

        context.ScheduleWindows.Remove(window);
        await context.SaveChangesAsync(cancellationToken);
    }
}
