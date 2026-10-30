using EduFocus.Ports.DTOs;

namespace EduFocus.Ports.Inbound;

/// <summary>
/// Data and privacy screen (UC11, issue #38). Reading and saving both require
/// an authenticated Parent Mode session; every change is audited.
/// </summary>
public interface IPrivacySettingsUseCase
{
    Task<PrivacySnapshot> GetSnapshotAsync(CancellationToken cancellationToken = default);

    Task<PrivacySettingsSaveResult> SaveAsync(
        PrivacySettings settings,
        CancellationToken cancellationToken = default);
}
