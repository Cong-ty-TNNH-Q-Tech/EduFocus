using EduFocus.Domain.Entities;
using EduFocus.Domain.Enums;
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

    public async Task<IReadOnlyList<AuditLog>> ListByTypeSinceAsync(
        AuditEventType eventType,
        DateTime fromUtc,
        int limit,
        CancellationToken cancellationToken = default)
    {
        if (limit <= 0)
            throw new ArgumentOutOfRangeException(nameof(limit));

        await using var context = _contextFactory.CreateDbContext();

        // IX_AuditLogs_EventType keeps the tamper scan narrow; only markers are
        // read and descriptions never leave the device.
        return await context.AuditLogs
            .AsNoTracking()
            .Where(x => x.EventType == eventType && x.OccurredAtUtc >= fromUtc)
            .OrderBy(x => x.CreatedAtUtc)
            .ThenBy(x => x.Id)
            .Take(limit)
            .ToListAsync(cancellationToken);
    }
}
