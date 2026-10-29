using EduFocus.Domain.Entities;
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
}
