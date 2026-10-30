using EduFocus.Ports.DTOs;

namespace EduFocus.Ports.Inbound;

/// <summary>
/// Parent credential setup, login, PIN change and recovery (UC01, UC02, UC13).
/// Plaintext secrets only cross this boundary; they are never stored or logged.
/// </summary>
public interface IParentAuthUseCase
{
    Task<bool> IsConfiguredAsync(CancellationToken cancellationToken = default);

    Task<ParentCredentialSetupResult> SetupAsync(string pin, CancellationToken cancellationToken = default);

    Task<ParentAuthResult> VerifyPinAsync(string pin, CancellationToken cancellationToken = default);

    Task<ParentAuthResult> ChangePinAsync(
        string currentPin,
        string newPin,
        CancellationToken cancellationToken = default);

    Task<ParentPinResetResult> ResetPinWithRecoveryCodeAsync(
        string recoveryCode,
        string newPin,
        CancellationToken cancellationToken = default);

    /// <summary>Issues a new recovery code after verifying the current PIN (UC01 exception).</summary>
    Task<ParentRecoveryCodeResult> RegenerateRecoveryCodeAsync(
        string pin,
        CancellationToken cancellationToken = default);
}
