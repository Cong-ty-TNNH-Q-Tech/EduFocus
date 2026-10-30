using EduFocus.Ports.DTOs;

namespace EduFocus.Ports.Inbound;

/// <summary>
/// Parent Mode session around <see cref="IParentAuthUseCase"/> (UC02, UC13):
/// a successful login starts the session, a PIN change ends it and a reset
/// leaves the parent logged out.
/// </summary>
public interface IParentModeUseCase
{
    bool IsAuthenticated { get; }

    Task<bool> IsConfiguredAsync(CancellationToken cancellationToken = default);

    Task<ParentAuthResult> LoginAsync(string pin, CancellationToken cancellationToken = default);

    Task<ParentAuthResult> ChangePinAsync(
        string currentPin,
        string newPin,
        CancellationToken cancellationToken = default);

    Task<ParentPinResetResult> ResetPinWithRecoveryCodeAsync(
        string recoveryCode,
        string newPin,
        CancellationToken cancellationToken = default);

    void RecordActivity();

    void Lock();
}
