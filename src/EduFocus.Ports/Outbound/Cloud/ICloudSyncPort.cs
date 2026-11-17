namespace EduFocus.Ports.Outbound.Cloud;

/// <summary>
/// HTTP boundary for the optional cloud relay (pairing, link status, revoke).
/// Implementations must apply a 15 second timeout, TLS, a stable
/// <c>EduFocus/{version}</c> User-Agent and must never log tokens. The outbox
/// worker (#87) reuses this port for sync ingestion.
/// </summary>
public interface ICloudSyncPort
{
    Task<CloudPairingSession> CreatePairingSessionAsync(
        Uri cloudBaseUrl,
        CloudPairingRequest request,
        CancellationToken cancellationToken = default);

    Task<CloudPairingSessionStatus> GetPairingSessionStatusAsync(
        Uri cloudBaseUrl,
        Guid sessionId,
        string desktopToken,
        CancellationToken cancellationToken = default);

    Task<CloudRemoteLinkStatus> GetLinkStatusAsync(
        Uri cloudBaseUrl,
        string desktopToken,
        CancellationToken cancellationToken = default);

    Task<CloudSyncBatchResult> SendEventsAsync(
        Uri cloudBaseUrl,
        string desktopToken,
        IReadOnlyList<CloudSyncEvent> events,
        CancellationToken cancellationToken = default);

    Task RevokeDeviceAsync(
        Uri cloudBaseUrl,
        string desktopToken,
        Guid deviceId,
        CancellationToken cancellationToken = default);

    Task DeleteFamilyAsync(
        Uri cloudBaseUrl,
        string desktopToken,
        CancellationToken cancellationToken = default);
}
