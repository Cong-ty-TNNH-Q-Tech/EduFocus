using EduFocus.Domain.Entities;
using EduFocus.Domain.Enums;
using EduFocus.Ports.Outbound.Persistence;

namespace EduFocus.UseCases.Tests.TestSupport;

internal sealed class RecordingAuditLogRepository : IAuditLogRepository
{
    public List<AuditLog> Entries { get; } = [];

    public bool ThrowOnAdd { get; set; }

    public bool ThrowOnList { get; set; }

    public int ListCalls { get; private set; }

    public AuditEventType? LastEventType { get; private set; }

    public DateTime? LastFromUtc { get; private set; }

    public int? LastLimit { get; private set; }

    public Task AddAsync(AuditLog auditLog, CancellationToken cancellationToken = default)
    {
        if (ThrowOnAdd)
            throw new InvalidOperationException("The audit log is unavailable.");

        Entries.Add(auditLog);
        return Task.CompletedTask;
    }

    public Task<IReadOnlyList<AuditLog>> ListByTypeSinceAsync(
        AuditEventType eventType,
        DateTime fromUtc,
        int limit,
        CancellationToken cancellationToken = default)
    {
        ListCalls++;
        LastEventType = eventType;
        LastFromUtc = fromUtc;
        LastLimit = limit;

        if (ThrowOnList)
            throw new InvalidOperationException("The audit log is unavailable.");

        IReadOnlyList<AuditLog> result = Entries
            .Where(entry => entry.EventType == eventType && entry.OccurredAtUtc >= fromUtc)
            .OrderBy(entry => entry.CreatedAtUtc)
            .ThenBy(entry => entry.Id.Value)
            .Take(limit)
            .ToList();

        return Task.FromResult(result);
    }
}
