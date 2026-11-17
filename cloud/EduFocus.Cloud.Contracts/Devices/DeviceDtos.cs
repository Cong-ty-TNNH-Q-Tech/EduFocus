namespace EduFocus.Cloud.Contracts.Devices;

public sealed record DeviceDto(
    Guid Id,
    string Kind,
    string Name,
    string Platform,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset? LastSeenAtUtc);

public sealed record DeviceListResponse(IReadOnlyList<DeviceDto> Devices);
