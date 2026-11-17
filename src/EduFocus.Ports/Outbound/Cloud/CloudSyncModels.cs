using System.Text.Json;

namespace EduFocus.Ports.Outbound.Cloud;

public enum CloudEventOutcome
{
    Applied,
    Duplicate,
    Rejected,
}

public sealed record CloudSyncEvent(
    string ClientEventId,
    string Type,
    DateTimeOffset OccurredAtUtc,
    JsonElement Payload);

public sealed record CloudSyncEventResult(
    string ClientEventId,
    CloudEventOutcome Outcome,
    string? Code,
    string? Message);

public sealed record CloudSyncBatchResult(
    IReadOnlyList<CloudSyncEventResult> Results,
    DateTimeOffset ServerTimeUtc);

public sealed record CloudPairingRequest(
    string DesktopName,
    string Platform,
    string? AppVersion,
    string? TimeZoneId);

public sealed record CloudPairingSession(
    Guid SessionId,
    string Code,
    string PairingUri,
    DateTime ExpiresAtUtc,
    string DesktopToken,
    string CloudBaseUrl);

public enum CloudPairingStatus
{
    Pending,
    Claimed,
    Expired,
    Cancelled,
}

public sealed record CloudPairingSessionStatus(
    Guid SessionId,
    CloudPairingStatus Status,
    DateTime ExpiresAtUtc,
    DateTime? ClaimedAtUtc,
    string? MobileDeviceName,
    Guid? MobileDeviceId);

public sealed record CloudRemoteDevice(
    Guid Id,
    string Kind,
    string Name,
    string Platform,
    DateTime CreatedAtUtc,
    DateTime? LastSeenAtUtc,
    bool IsRevoked);

public sealed record CloudRemoteLinkStatus(
    bool Linked,
    Guid? FamilyId,
    Guid? DeviceId,
    string? TimeZoneId,
    DateTime? LinkedAtUtc,
    DateTime? LastSyncAtUtc,
    int PendingSyncEvents,
    IReadOnlyList<CloudRemoteDevice> Devices);
