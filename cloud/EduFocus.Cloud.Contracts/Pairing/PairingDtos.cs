namespace EduFocus.Cloud.Contracts.Pairing;

public enum PairingSessionStatus
{
    Pending,
    Claimed,
    Expired,
    Cancelled,
}

public sealed record CreatePairingSessionRequest(
    string DesktopName,
    string Platform,
    string? AppVersion = null,
    string? TimeZoneId = null);

public sealed record PairingSessionResponse(
    Guid SessionId,
    string Code,
    string PairingUri,
    DateTimeOffset ExpiresAtUtc,
    string DesktopToken,
    string CloudBaseUrl);

public sealed record PairingSessionStatusResponse(
    Guid SessionId,
    PairingSessionStatus Status,
    DateTimeOffset ExpiresAtUtc,
    DateTimeOffset? ClaimedAtUtc = null,
    string? MobileDeviceName = null,
    Guid? MobileDeviceId = null);

public sealed record ClaimPairingRequest(
    string Code,
    string DeviceName,
    string Platform,
    string? AppVersion = null,
    string? SessionId = null,
    string? TimeZoneId = null);

public sealed record ClaimPairingResponse(
    string MobileToken,
    Guid FamilyId,
    Guid DeviceId,
    string DesktopName,
    string TimeZoneId,
    DateTimeOffset LinkedAtUtc);

public sealed record LinkedDeviceDto(
    Guid Id,
    string Kind,
    string Name,
    string Platform,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset? LastSeenAtUtc,
    DateTimeOffset? RevokedAtUtc);

public sealed record LinkStatusResponse(
    bool Linked,
    Guid? FamilyId,
    Guid? DeviceId,
    string? TimeZoneId,
    DateTimeOffset? LinkedAtUtc,
    DateTimeOffset? LastSyncAtUtc,
    int PendingSyncEvents,
    IReadOnlyList<LinkedDeviceDto> Devices);
