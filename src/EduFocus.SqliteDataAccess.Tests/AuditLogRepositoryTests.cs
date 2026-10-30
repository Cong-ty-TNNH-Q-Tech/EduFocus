using EduFocus.Domain.Entities;
using EduFocus.Domain.Enums;
using EduFocus.SqliteDataAccess.Repositories;
using EduFocus.SqliteDataAccess.Tests.TestSupport;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;

namespace EduFocus.SqliteDataAccess.Tests;

public sealed class AuditLogRepositoryTests
{
    [Fact]
    public async Task AddAsync_persists_audit_event_with_correlation_id()
    {
        await using var db = new TempDatabase();
        await db.CreateInitializer().InitializeAsync();

        var correlationId = Guid.NewGuid();
        var occurredAt = new DateTime(2026, 10, 29, 9, 30, 0, DateTimeKind.Utc);
        var auditLog = AuditLog.Record(
            AuditActor.Parent,
            AuditEventType.PinSetup,
            "Parent PIN configured",
            occurredAt,
            correlationId: correlationId);

        var repository = new SqliteAuditLogRepository(db.ContextFactory);
        await repository.AddAsync(auditLog);

        await using var context = db.CreateContext();
        var stored = await context.AuditLogs.SingleAsync();

        stored.Id.Should().Be(auditLog.Id);
        stored.Actor.Should().Be(AuditActor.Parent);
        stored.EventType.Should().Be(AuditEventType.PinSetup);
        stored.CorrelationId.Should().Be(correlationId);
        stored.OccurredAtUtc.Should().Be(occurredAt);
    }
}
