using EduFocus.Domain.Common;
using EduFocus.Domain.Privacy;
using EduFocus.Domain.ValueObjects;

namespace EduFocus.Domain.Entities;

/// <summary>
/// Singleton opt-in cloud relay link (ADR-20). The desktop bearer token lives
/// only in the SQLCipher-encrypted database and must never reach logs, exports,
/// exception messages or UI; audit metadata built from this entity excludes it
/// by construction at the use case boundary.
/// </summary>
public sealed class CloudLink
{
    public const int MaxBaseUrlLength = 512;
    public const int MaxPairingCodeLength = 32;
    public const int MaxTokenLength = 256;
    public const int MaxErrorLength = 512;

    private CloudLink(CloudLinkId id, DateTime createdAtUtc)
    {
        Id = id;
        IsEnabled = false;
        ConsentAcceptedAtUtc = null;
        CloudBaseUrl = null;
        PairingSessionId = null;
        PairingCode = null;
        PairingExpiresAtUtc = null;
        FamilyId = null;
        DesktopDeviceId = null;
        DesktopToken = null;
        LinkedAtUtc = null;
        LastSyncAtUtc = null;
        LastError = null;
        CreatedAtUtc = createdAtUtc;
        UpdatedAtUtc = createdAtUtc;
    }

    public CloudLinkId Id { get; }

    public bool IsEnabled { get; private set; }

    public DateTime? ConsentAcceptedAtUtc { get; private set; }

    public string? CloudBaseUrl { get; private set; }

    public Guid? PairingSessionId { get; private set; }

    public string? PairingCode { get; private set; }

    public DateTime? PairingExpiresAtUtc { get; private set; }

    public Guid? FamilyId { get; private set; }

    public Guid? DesktopDeviceId { get; private set; }

    public string? DesktopToken { get; private set; }

    public DateTime? LinkedAtUtc { get; private set; }

    public DateTime? LastSyncAtUtc { get; private set; }

    public string? LastError { get; private set; }

    public DateTime CreatedAtUtc { get; }

    public DateTime UpdatedAtUtc { get; private set; }

    public bool HasConsent => ConsentAcceptedAtUtc is not null;

    public bool IsLinked =>
        IsEnabled
        && FamilyId is not null
        && DesktopDeviceId is not null
        && !string.IsNullOrEmpty(DesktopToken);

    public static CloudLink CreateDefault(DateTime nowUtc)
    {
        Guard.Utc(nowUtc, nameof(nowUtc));
        return new CloudLink(CloudLinkId.New(), nowUtc);
    }

    public bool HasActivePairing(DateTime nowUtc) =>
        IsEnabled
        && PairingSessionId is not null
        && PairingExpiresAtUtc is { } expiresAt
        && expiresAt > nowUtc;

    public void RecordConsent(DateTime nowUtc)
    {
        Guard.Utc(nowUtc, nameof(nowUtc));
        ConsentAcceptedAtUtc = nowUtc;
        Touch(nowUtc);
    }

    public void RevokeConsent(DateTime nowUtc)
    {
        Guard.Utc(nowUtc, nameof(nowUtc));

        if (IsEnabled)
            throw new InvalidOperationException("Disable the cloud link before revoking consent.");

        ConsentAcceptedAtUtc = null;
        ClearLinkState();
        Touch(nowUtc);
    }

    public void BeginPairing(
        string cloudBaseUrl,
        Guid sessionId,
        string pairingCode,
        string desktopToken,
        DateTime expiresAtUtc,
        DateTime nowUtc)
    {
        Guard.NotNullOrWhiteSpace(cloudBaseUrl, nameof(cloudBaseUrl));
        Guard.MaxLength(cloudBaseUrl, MaxBaseUrlLength, nameof(cloudBaseUrl));
        Guard.NotNullOrWhiteSpace(pairingCode, nameof(pairingCode));
        Guard.MaxLength(pairingCode, MaxPairingCodeLength, nameof(pairingCode));
        Guard.NotNullOrWhiteSpace(desktopToken, nameof(desktopToken));
        Guard.MaxLength(desktopToken, MaxTokenLength, nameof(desktopToken));
        Guard.Utc(expiresAtUtc, nameof(expiresAtUtc));
        Guard.Utc(nowUtc, nameof(nowUtc));

        if (!HasConsent)
            throw new InvalidOperationException("Cloud consent is required before pairing.");
        if (sessionId == Guid.Empty)
            throw new ArgumentException("Pairing session id is required.", nameof(sessionId));
        if (HasActivePairing(nowUtc))
            throw new InvalidOperationException("A pairing session is already active.");
        if (expiresAtUtc <= nowUtc)
            throw new ArgumentException("Pairing expiry must be in the future.", nameof(expiresAtUtc));

        IsEnabled = true;
        CloudBaseUrl = cloudBaseUrl.TrimEnd('/');
        PairingSessionId = sessionId;
        PairingCode = pairingCode.Trim().ToUpperInvariant();
        PairingExpiresAtUtc = expiresAtUtc;
        DesktopToken = desktopToken;
        FamilyId = null;
        DesktopDeviceId = null;
        LinkedAtUtc = null;
        LastSyncAtUtc = null;
        LastError = null;
        Touch(nowUtc);
    }

    public void CompletePairing(Guid familyId, Guid desktopDeviceId, DateTime linkedAtUtc, DateTime nowUtc)
    {
        Guard.Utc(linkedAtUtc, nameof(linkedAtUtc));
        Guard.Utc(nowUtc, nameof(nowUtc));

        if (familyId == Guid.Empty)
            throw new ArgumentException("Family id is required.", nameof(familyId));
        if (desktopDeviceId == Guid.Empty)
            throw new ArgumentException("Desktop device id is required.", nameof(desktopDeviceId));
        if (!IsEnabled || string.IsNullOrEmpty(DesktopToken))
            throw new InvalidOperationException("Cloud pairing has not been started.");

        FamilyId = familyId;
        DesktopDeviceId = desktopDeviceId;
        LinkedAtUtc = linkedAtUtc;
        PairingSessionId = null;
        PairingCode = null;
        PairingExpiresAtUtc = null;
        LastError = null;
        Touch(nowUtc);
    }

    public void RecordSync(DateTime lastSyncAtUtc, DateTime nowUtc)
    {
        Guard.Utc(lastSyncAtUtc, nameof(lastSyncAtUtc));
        Guard.Utc(nowUtc, nameof(nowUtc));

        if (!IsLinked)
            throw new InvalidOperationException("Cloud link is not paired.");

        LastSyncAtUtc = lastSyncAtUtc;
        LastError = null;
        Touch(nowUtc);
    }

    public void RecordError(string? message, DateTime nowUtc)
    {
        Guard.Utc(nowUtc, nameof(nowUtc));
        LastError = TextSanitizer.Clean(message, MaxErrorLength);
        Touch(nowUtc);
    }

    /// <summary>Stops syncing and forgets every token or identifier for the link.</summary>
    public void Unlink(DateTime nowUtc)
    {
        Guard.Utc(nowUtc, nameof(nowUtc));
        IsEnabled = false;
        ClearLinkState();
        Touch(nowUtc);
    }

    private void ClearLinkState()
    {
        PairingSessionId = null;
        PairingCode = null;
        PairingExpiresAtUtc = null;
        FamilyId = null;
        DesktopDeviceId = null;
        DesktopToken = null;
        LinkedAtUtc = null;
        LastSyncAtUtc = null;
        LastError = null;
    }

    private void Touch(DateTime nowUtc)
    {
        Guard.Utc(nowUtc, nameof(nowUtc));

        if (nowUtc < UpdatedAtUtc)
            throw new ArgumentException("Timestamp must not move backwards.", nameof(nowUtc));

        UpdatedAtUtc = nowUtc;
    }
}
