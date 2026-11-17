namespace EduFocus.Cloud.Contracts.Pairing;

public enum PairingSessionStatus
{
    Pending,
    Claimed,
    Expired,
    Cancelled,
}

public sealed record CreatePairingSessionRequest(
    string DeviceName,
    string Platform,
    string? AppVersion = null);

public sealed record PairingSessionResponse(
    Guid SessionId,
    string Code,
    string PairingUri,
    DateTimeOffset ExpiresAtUtc);

public sealed record PairingSessionStatusResponse(
    Guid SessionId,
    PairingSessionStatus Status,
    DateTimeOffset ExpiresAtUtc,
    string? DesktopToken = null,
    Guid? FamilyId = null,
    Guid? DeviceId = null,
    DateTimeOffset? ClaimedAtUtc = null);

public sealed record ClaimPairingRequest(
    string Code,
    string DeviceName,
    string Platform,
    string? SessionId = null,
    string? PushToken = null,
    string? AppVersion = null);

public sealed record ClaimPairingResponse(
    string MobileToken,
    Guid FamilyId,
    Guid DeviceId,
    string TimeZone,
    DateTimeOffset LinkedAtUtc);

public sealed record LinkStatusResponse(
    bool Linked,
    Guid? FamilyId = null,
    Guid? DeviceId = null,
    string? TimeZone = null,
    DateTimeOffset? LinkedAtUtc = null);
