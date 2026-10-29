using EduFocus.Domain.Entities;
using EduFocus.Domain.Enums;
using FluentAssertions;

namespace EduFocus.Domain.Tests.Domain;

public class AuditLogTests
{
    private static readonly DateTime Now = new(2026, 10, 29, 7, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void Record_StoresAuditEntry()
    {
        var correlationId = Guid.NewGuid();

        var log = AuditLog.Record(
            AuditActor.Parent,
            AuditEventType.ProtectionOn,
            "  Bật bảo vệ\r\ntheo lịch  ",
            Now,
            metadataJson: "{ \"source\": \"schedule\" }",
            correlationId,
            createdAtUtc: Now.AddSeconds(1));

        log.Actor.Should().Be(AuditActor.Parent);
        log.EventType.Should().Be(AuditEventType.ProtectionOn);
        log.Description.Should().Be("Bật bảo vệ  theo lịch");
        log.MetadataJson.Should().Be("{ \"source\": \"schedule\" }");
        log.CorrelationId.Should().Be(correlationId);
        log.OccurredAtUtc.Should().Be(Now);
        log.CreatedAtUtc.Should().Be(Now.AddSeconds(1));
    }

    [Fact]
    public void Record_RequiresDescription()
    {
        var blank = () => AuditLog.Record(
            AuditActor.System, AuditEventType.SettingsChanged, "   ", Now);
        blank.Should().Throw<ArgumentException>().WithParameterName("description");

        var tooLong = () => AuditLog.Record(
            AuditActor.System,
            AuditEventType.SettingsChanged,
            new string('d', AuditLog.MaxDescriptionLength + 1),
            Now);
        tooLong.Should().Throw<ArgumentException>().WithParameterName("description");
    }

    [Fact]
    public void Record_RejectsOversizedMetadata()
    {
        var act = () => AuditLog.Record(
            AuditActor.System,
            AuditEventType.SettingsChanged,
            "Settings changed",
            Now,
            metadataJson: new string('m', AuditLog.MaxMetadataLength + 1));

        act.Should().Throw<ArgumentException>().WithParameterName("metadataJson");
    }

    [Fact]
    public void Record_RejectsEmptyCorrelationId()
    {
        var act = () => AuditLog.Record(
            AuditActor.System,
            AuditEventType.SettingsChanged,
            "Settings changed",
            Now,
            correlationId: Guid.Empty);

        act.Should().Throw<ArgumentException>().WithParameterName("correlationId");
    }

    [Fact]
    public void Record_RejectsCreationBeforeOccurrence()
    {
        var act = () => AuditLog.Record(
            AuditActor.System,
            AuditEventType.SettingsChanged,
            "Settings changed",
            Now,
            createdAtUtc: Now.AddSeconds(-1));

        act.Should().Throw<ArgumentException>().WithParameterName("createdAtUtc");
    }

    [Fact]
    public void Record_RejectsUndefinedEnums()
    {
        var actor = () => AuditLog.Record(
            (AuditActor)99, AuditEventType.ProtectionOn, "x", Now);
        actor.Should().Throw<ArgumentOutOfRangeException>();

        var eventType = () => AuditLog.Record(
            AuditActor.System, (AuditEventType)99, "x", Now);
        eventType.Should().Throw<ArgumentOutOfRangeException>();
    }
}
