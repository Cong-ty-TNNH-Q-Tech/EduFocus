using EduFocus.Domain.Entities;
using EduFocus.Domain.Enums;
using EduFocus.Domain.ValueObjects;
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
        CancellationToken cancellationToken = default) =>
        ListByTypeSinceAsync(eventType, fromUtc, limit, null, null, cancellationToken);

    public Task<IReadOnlyList<AuditLog>> ListByTypeSinceAsync(
        AuditEventType eventType,
        DateTime fromUtc,
        int limit,
        DateTime? afterCreatedAtUtc,
        AuditLogId? afterId,
        CancellationToken cancellationToken = default)
    {
        ListCalls++;
        LastEventType = eventType;
        LastFromUtc = fromUtc;
        LastLimit = limit;

        if (ThrowOnList)
            throw new InvalidOperationException("The audit log is unavailable.");

        var query = Entries
            .Where(entry => entry.EventType == eventType && entry.OccurredAtUtc >= fromUtc);

        if (afterCreatedAtUtc is { } cursorTime && afterId is { } cursorId)
        {
            query = query.Where(entry =>
                entry.CreatedAtUtc > cursorTime
                || (entry.CreatedAtUtc == cursorTime && entry.Id.Value.CompareTo(cursorId.Value) > 0));
        }

        IReadOnlyList<AuditLog> result = query
            .OrderBy(entry => entry.CreatedAtUtc)
            .ThenBy(entry => entry.Id.Value)
            .Take(limit)
            .ToList();

        return Task.FromResult(result);
    }
}
