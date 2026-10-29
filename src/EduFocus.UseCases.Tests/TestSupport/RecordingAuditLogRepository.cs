using EduFocus.Domain.Entities;
using EduFocus.Ports.Outbound.Persistence;

namespace EduFocus.UseCases.Tests.TestSupport;

internal sealed class RecordingAuditLogRepository : IAuditLogRepository
{
    public List<AuditLog> Entries { get; } = [];

    public Task AddAsync(AuditLog auditLog, CancellationToken cancellationToken = default)
    {
        Entries.Add(auditLog);
        return Task.CompletedTask;
    }
}
