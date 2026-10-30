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
    private readonly SqliteDbContextFactory _contextFactory;

    public SqliteAuditLogRepository(SqliteDbContextFactory contextFactory)
    {
        ArgumentNullException.ThrowIfNull(contextFactory);
        _contextFactory = contextFactory;
    }

    public async Task AddAsync(AuditLog auditLog, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(auditLog);

        await using var context = _contextFactory.CreateDbContext();
        context.AuditLogs.Add(auditLog);
        await context.SaveChangesAsync(cancellationToken);
    }
}
