using EduFocus.Domain.Entities;
using EduFocus.Ports.Outbound.Persistence;
using EduFocus.SqliteDataAccess.Persistence;
using Microsoft.EntityFrameworkCore;

namespace EduFocus.SqliteDataAccess.Repositories;

/// <summary>
/// SQLite-backed persistence for parent/system audit events. Technical file
/// logs never replace these rows (database schema §12).
/// </summary>
public sealed class SqliteAuditLogRepository : IAuditLogRepository
{
    private readonly DbContextOptions<EduFocusDbContext> _dbContextOptions;

    public SqliteAuditLogRepository(EduFocusDatabaseOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        _dbContextOptions = new DbContextOptionsBuilder<EduFocusDbContext>()
            .UseSqlite(options.ConnectionString)
            .AddInterceptors(new SqlitePragmaInterceptor())
            .Options;
    }

    public async Task AddAsync(AuditLog auditLog, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(auditLog);

        await using var context = new EduFocusDbContext(_dbContextOptions);
        context.AuditLogs.Add(auditLog);
        await context.SaveChangesAsync(cancellationToken);
    }
}
