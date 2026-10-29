using EduFocus.Ports.DTOs;
using EduFocus.Ports.Inbound;

namespace EduFocus.UseCases.Auth;

/// <summary>
/// Applies Parent Mode session rules to credential operations. A successful
/// PIN change or recovery reset ends the session so the parent must log in
/// again with the new PIN (UC13).
/// </summary>
public sealed class ParentModeService : IParentModeUseCase
{
    private readonly IParentAuthUseCase _auth;
    private readonly ParentSessionState _session;

    public ParentModeService(IParentAuthUseCase auth, ParentSessionState session)
    {
        _auth = auth ?? throw new ArgumentNullException(nameof(auth));
        _session = session ?? throw new ArgumentNullException(nameof(session));
    }

    public bool IsAuthenticated => _session.IsAuthenticated;

    public Task<bool> IsConfiguredAsync(CancellationToken cancellationToken = default) =>
        _auth.IsConfiguredAsync(cancellationToken);

    public async Task<ParentAuthResult> LoginAsync(
        string pin,
        CancellationToken cancellationToken = default)
    {
        var result = await _auth.VerifyPinAsync(pin, cancellationToken);

        if (result.Status == ParentAuthStatus.Success)
            _session.Authenticate();

        return result;
    }

    public async Task<ParentAuthResult> ChangePinAsync(
        string currentPin,
        string newPin,
        CancellationToken cancellationToken = default)
    {
        _session.EnsureAuthorized("change PIN");

        var result = await _auth.ChangePinAsync(currentPin, newPin, cancellationToken);

        if (result.Status == ParentAuthStatus.Success)
            _session.Lock();

        return result;
    }

    public async Task<ParentPinResetResult> ResetPinWithRecoveryCodeAsync(
        string recoveryCode,
        string newPin,
        CancellationToken cancellationToken = default)
    {
        _session.Lock();
        return await _auth.ResetPinWithRecoveryCodeAsync(recoveryCode, newPin, cancellationToken);
    }

    public void RecordActivity() => _session.RecordActivity();

    public void Lock() => _session.Lock();
}
