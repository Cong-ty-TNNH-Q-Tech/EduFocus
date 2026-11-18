using EduFocus.Domain.Entities;
using EduFocus.Domain.Enums;
using EduFocus.Domain.ValueObjects;

namespace EduFocus.Ports.Outbound.Persistence;

/// <summary>
/// Persists parent/system audit events in the AuditLogs table. Audit entries
/// are separate from the technical file log (database schema §12).
/// </summary>
public interface IAuditLogRepository
{
    Task AddAsync(AuditLog auditLog, CancellationToken cancellationToken = default);

    /// <summary>
    /// Lists the oldest rows of one event type at or after
    /// <paramref name="fromUtc"/>. Used by the cloud security producer, which
    /// reads only tamper markers and never exposes audit text off the device.
    /// </summary>
    Task<IReadOnlyList<AuditLog>> ListByTypeSinceAsync(
        AuditEventType eventType,
        DateTime fromUtc,
        int limit,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Keyset-paginated variant of <see cref="ListByTypeSinceAsync(AuditEventType, DateTime, int, CancellationToken)"/>
    /// starting strictly after <paramref name="afterCreatedAtUtc"/> / <paramref name="afterId"/>.
    /// </summary>
    Task<IReadOnlyList<AuditLog>> ListByTypeSinceAsync(
        AuditEventType eventType,
        DateTime fromUtc,
        int limit,
        DateTime? afterCreatedAtUtc,
        AuditLogId? afterId,
        CancellationToken cancellationToken = default);
}
