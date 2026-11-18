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

    [Fact]
    public async Task ListByTypeSince_filters_by_type_and_time_in_creation_order()
    {
        await using var db = new TempDatabase();
        await db.CreateInitializer().InitializeAsync();

        var repository = new SqliteAuditLogRepository(db.ContextFactory);
        var from = new DateTime(2026, 11, 17, 9, 0, 0, DateTimeKind.Utc);
        var older = AuditLog.Record(
            AuditActor.System, AuditEventType.TamperDetected, "before the window", from.AddMinutes(-1));
        var first = AuditLog.Record(
            AuditActor.System, AuditEventType.TamperDetected, "first", from);
        var second = AuditLog.Record(
            AuditActor.System, AuditEventType.TamperDetected, "second", from.AddMinutes(1));
        var other = AuditLog.Record(
            AuditActor.Parent, AuditEventType.ProtectionOff, "other type", from.AddMinutes(2));
        await repository.AddAsync(older);
        await repository.AddAsync(first);
        await repository.AddAsync(second);
        await repository.AddAsync(other);

        var result = await repository.ListByTypeSinceAsync(AuditEventType.TamperDetected, from, 10);
        var limited = await repository.ListByTypeSinceAsync(AuditEventType.TamperDetected, from, 1);

        result.Should().HaveCount(2);
        result.Select(entry => entry.Id).Should().ContainInOrder(first.Id, second.Id);
        limited.Should().ContainSingle().Which.Id.Should().Be(first.Id);

        var page2 = await repository.ListByTypeSinceAsync(
            AuditEventType.TamperDetected,
            from,
            10,
            limited[0].CreatedAtUtc,
            limited[0].Id);
        page2.Should().ContainSingle().Which.Id.Should().Be(second.Id);
    }
}
