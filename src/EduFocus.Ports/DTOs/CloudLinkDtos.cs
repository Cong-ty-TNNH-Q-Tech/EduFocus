namespace EduFocus.Ports.DTOs;

/// <summary>Input for enabling the optional cloud relay. Never contains tokens.</summary>
public sealed record CloudLinkOptions(
    string CloudBaseUrl,
    string DesktopName,
    string Platform = "windows",
    string? AppVersion = null,
    string? TimeZoneId = null);

/// <summary>
/// Pairing material shown to the parent (UC15). The desktop token stays in the
/// encrypted database and is deliberately not part of this record.
/// </summary>
public sealed record CloudPairingInfo(
    Guid SessionId,
    string Code,
    DateTime ExpiresAtUtc,
    string QrPayload,
    string CloudBaseUrl);

public sealed record CloudLinkedDevice(
    Guid Id,
    string Kind,
    string Name,
    string Platform,
    bool IsRevoked);

/// <summary>Local link state; safe to bind to the UI because it has no token.</summary>
public sealed record CloudLinkStatusSnapshot(
    bool IsEnabled,
    bool HasConsent,
    DateTime? ConsentAcceptedAtUtc,
    string? CloudBaseUrl,
    bool IsLinked,
    Guid? FamilyId,
    Guid? DesktopDeviceId,
    DateTime? LinkedAtUtc,
    DateTime? LastSyncAtUtc,
    string? LastError,
    CloudPairingInfo? ActivePairing,
    IReadOnlyList<CloudLinkedDevice> Devices,
    int PendingSyncEvents,
    bool RemoteStatusChecked);
