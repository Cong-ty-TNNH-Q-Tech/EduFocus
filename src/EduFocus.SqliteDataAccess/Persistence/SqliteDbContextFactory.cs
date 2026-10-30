using Microsoft.EntityFrameworkCore;

namespace EduFocus.SqliteDataAccess.Persistence;

/// <summary>
/// Builds <see cref="EduFocusDbContext"/> instances whose connections apply the
/// SQLCipher key and per-connection pragmas. Repositories share one factory so
/// a missing key fails closed before any query runs.
/// </summary>
public sealed class SqliteDbContextFactory
{
    private readonly DbContextOptions<EduFocusDbContext> _options;

    public SqliteDbContextFactory(EduFocusDatabaseOptions options, IDatabaseKeyStore keyStore)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(keyStore);

        _options = new DbContextOptionsBuilder<EduFocusDbContext>()
            .UseSqlite(options.ConnectionString)
            .AddInterceptors(new SqlitePragmaInterceptor(keyStore))
            .Options;
    }

    public EduFocusDbContext CreateDbContext() => new(_options);
}
