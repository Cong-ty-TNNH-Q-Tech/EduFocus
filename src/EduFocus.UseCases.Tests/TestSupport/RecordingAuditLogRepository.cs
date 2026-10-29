using EduFocus.Domain.Entities;
using EduFocus.Ports.Outbound.Persistence;

namespace EduFocus.UseCases.Tests.TestSupport;

internal sealed class RecordingAuditLogRepository : IAuditLogRepository
{
    public List<AuditLog> Entries { get; } = [];

    public bool ThrowOnAdd { get; set; }

    public Task AddAsync(AuditLog auditLog, CancellationToken cancellationToken = default)
    {
        if (ThrowOnAdd)
            throw new InvalidOperationException("The audit log is unavailable.");

        Entries.Add(auditLog);
        return Task.CompletedTask;
    }
}
