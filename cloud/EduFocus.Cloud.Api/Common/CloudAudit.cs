using System.Text.Json;
using EduFocus.Cloud.Contracts.Json;
using EduFocus.Cloud.Data.Entities;

namespace EduFocus.Cloud.Api.Common;

public static class CloudAudit
{
    public static CloudAuditLog Create(
        Guid? familyId,
        Guid? actorDeviceId,
        string action,
        DateTimeOffset createdAtUtc,
        object? metadata = null) =>
        new()
        {
            Id = Guid.NewGuid(),
            FamilyId = familyId,
            ActorDeviceId = actorDeviceId,
            Action = action,
            MetadataJson = JsonSerializer.Serialize(metadata ?? new { }, ApiJson.Options),
            CreatedAtUtc = createdAtUtc,
        };
}
