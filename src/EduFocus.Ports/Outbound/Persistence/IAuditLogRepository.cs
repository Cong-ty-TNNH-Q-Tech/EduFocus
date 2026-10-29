using EduFocus.Domain.Entities;

namespace EduFocus.Ports.Outbound.Persistence;

/// <summary>
/// Persists parent/system audit events in the AuditLogs table. Audit entries
/// are separate from the technical file log (database schema §12).
/// </summary>
public interface IAuditLogRepository
{
    Task AddAsync(AuditLog auditLog, CancellationToken cancellationToken = default);
}
