using EduFocus.Ports.DTOs;

namespace EduFocus.Ports.Inbound;

/// <summary>
/// Optional cloud relay link (UC15, UC18, ADR-20). Every method that changes
/// state requires an authenticated Parent Mode session; reading status is safe
/// without one and never contacts the network while the cloud is disabled.
/// </summary>
public interface ICloudLinkUseCase
{
    Task<CloudLinkStatusSnapshot> GetStatusAsync(CancellationToken cancellationToken = default);

    Task AcceptConsentAsync(CancellationToken cancellationToken = default);

    Task RevokeConsentAsync(CancellationToken cancellationToken = default);

    Task<CloudPairingInfo> EnableAsync(
        CloudLinkOptions options,
        CancellationToken cancellationToken = default);

    Task UnlinkAsync(bool deleteCloudData, CancellationToken cancellationToken = default);
}
