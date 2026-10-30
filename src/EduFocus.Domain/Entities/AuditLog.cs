using EduFocus.Domain.Common;
using EduFocus.Domain.Enums;
using EduFocus.Domain.Privacy;
using EduFocus.Domain.ValueObjects;

namespace EduFocus.Domain.Entities;

/// <summary>
/// Parental and system activity record. Callers must never pass PINs, recovery
/// codes, tokens or camera content; descriptions are single-line and truncated.
/// </summary>
public sealed class AuditLog
{
    public const int MaxDescriptionLength = 512;
    public const int MaxMetadataLength = 2048;

    private AuditLog(
        AuditLogId id,
        DateTime occurredAtUtc,
        AuditActor actor,
        AuditEventType eventType,
        string description,
        string? metadataJson,
        Guid? correlationId,
        DateTime createdAtUtc)
    {
        Id = id;
        OccurredAtUtc = occurredAtUtc;
        Actor = actor;
        EventType = eventType;
        Description = description;
        MetadataJson = metadataJson;
        CorrelationId = correlationId;
        CreatedAtUtc = createdAtUtc;
    }

    public AuditLogId Id { get; }

    public DateTime OccurredAtUtc { get; }

    public AuditActor Actor { get; }

    public AuditEventType EventType { get; }

    public string Description { get; }

    public string? MetadataJson { get; }

    public Guid? CorrelationId { get; }

    public DateTime CreatedAtUtc { get; }

    public static AuditLog Record(
        AuditActor actor,
        AuditEventType eventType,
        string description,
        DateTime occurredAtUtc,
        string? metadataJson = null,
        Guid? correlationId = null,
        DateTime? createdAtUtc = null)
    {
        Guard.Defined(actor, nameof(actor));
        Guard.Defined(eventType, nameof(eventType));
        Guard.NotNullOrWhiteSpace(description, nameof(description));
        Guard.MaxLength(description, MaxDescriptionLength, nameof(description));
        Guard.Utc(occurredAtUtc, nameof(occurredAtUtc));
        Guard.MaxLength(metadataJson, MaxMetadataLength, nameof(metadataJson));

        if (correlationId == Guid.Empty)
            throw new ArgumentException("Correlation id must not be empty.", nameof(correlationId));

        var created = createdAtUtc ?? occurredAtUtc;
        Guard.Utc(created, nameof(createdAtUtc));

        if (created < occurredAtUtc)
            throw new ArgumentException("Creation time must not precede the occurrence.", nameof(createdAtUtc));

        var cleanDescription = TextSanitizer.Clean(description, MaxDescriptionLength)
            ?? throw new ArgumentException("Description is required.", nameof(description));

        return new AuditLog(
            AuditLogId.New(),
            occurredAtUtc,
            actor,
            eventType,
            cleanDescription,
            TextSanitizer.Clean(metadataJson, MaxMetadataLength),
            correlationId,
            created);
    }
}
