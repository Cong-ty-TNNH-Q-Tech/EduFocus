using EduFocus.Ports.DTOs;

namespace EduFocus.Ports.Inbound;

/// <summary>
/// First-run setup (UC01). Privacy consent and a parent PIN are mandatory before
/// onboarding can be marked complete. Each step persists so a restart can resume.
/// </summary>
public interface IOnboardingUseCase
{
    Task<OnboardingState> GetStateAsync(CancellationToken cancellationToken = default);

    Task RecordPrivacyConsentAsync(CancellationToken cancellationToken = default);

    Task SavePreferencesAsync(
        OnboardingPreferences preferences,
        CancellationToken cancellationToken = default);

    Task<ParentCredentialSetupResult> ConfigurePinAsync(
        string pin,
        CancellationToken cancellationToken = default);

    /// <summary>Verifies the PIN and issues a new recovery code when a restart lost the first one.</summary>
    Task<ParentRecoveryCodeResult> ReissueRecoveryCodeAsync(
        string pin,
        CancellationToken cancellationToken = default);

    Task CompleteAsync(CancellationToken cancellationToken = default);
}
