using System.Text.Json;
using EduFocus.Domain.Entities;
using EduFocus.Domain.Enums;
using EduFocus.Domain.Security;
using EduFocus.Ports.DTOs;
using EduFocus.Ports.Inbound;
using EduFocus.Ports.Outbound.Persistence;
using EduFocus.Ports.Outbound.Services;

namespace EduFocus.UseCases.Auth;

/// <summary>
/// Parent credential lifecycle from UC01, UC02 and UC13. Secrets are hashed
/// before they reach persistence and are never written to an audit entry.
/// PIN and recovery-code failures share one lockout counter (BR-03).
/// </summary>
public sealed class ParentAuthUseCase : IParentAuthUseCase
{
    private readonly IParentCredentialRepository _credentials;
    private readonly IPasswordHasher _hasher;
    private readonly IRecoveryCodeGenerator _recoveryCodes;
    private readonly IAuditLogRepository _auditLogs;
    private readonly ICorrelationContext _correlation;
    private readonly TimeProvider _timeProvider;

    public ParentAuthUseCase(
        IParentCredentialRepository credentials,
        IPasswordHasher hasher,
        IRecoveryCodeGenerator recoveryCodes,
        IAuditLogRepository auditLogs,
        ICorrelationContext correlation,
        TimeProvider timeProvider)
    {
        _credentials = credentials ?? throw new ArgumentNullException(nameof(credentials));
        _hasher = hasher ?? throw new ArgumentNullException(nameof(hasher));
        _recoveryCodes = recoveryCodes ?? throw new ArgumentNullException(nameof(recoveryCodes));
        _auditLogs = auditLogs ?? throw new ArgumentNullException(nameof(auditLogs));
        _correlation = correlation ?? throw new ArgumentNullException(nameof(correlation));
        _timeProvider = timeProvider ?? throw new ArgumentNullException(nameof(timeProvider));
    }

    public async Task<bool> IsConfiguredAsync(CancellationToken cancellationToken = default)
    {
        return await _credentials.GetAsync(cancellationToken) is not null;
    }

    public async Task<ParentCredentialSetupResult> SetupAsync(
        string pin,
        CancellationToken cancellationToken = default)
    {
        ParentPinPolicy.EnsureValid(pin);

        if (await _credentials.GetAsync(cancellationToken) is not null)
            throw new InvalidOperationException("Parent credentials are already configured.");

        var now = UtcNow();
        var recoveryCode = _recoveryCodes.Generate();
        var pinHash = _hasher.Hash(pin);
        var recoveryHash = _hasher.Hash(ParentRecoveryCode.Normalize(recoveryCode));

        var credential = ParentCredential.Create(
            pinHash.HashBase64,
            pinHash.SaltBase64,
            pinHash.Iterations,
            recoveryHash.HashBase64,
            recoveryHash.SaltBase64,
            recoveryHash.Iterations,
            now);

        await _credentials.AddAsync(credential, cancellationToken);
        await WriteAuditAsync(
            AuditEventType.PinSetup,
            "Parent PIN created.",
            now,
            metadataJson: null,
            cancellationToken);

        return new ParentCredentialSetupResult(recoveryCode);
    }

    public async Task<ParentAuthResult> VerifyPinAsync(
        string pin,
        CancellationToken cancellationToken = default)
    {
        var now = UtcNow();
        var credential = await _credentials.GetAsync(cancellationToken);
        if (credential is null)
            return NotConfiguredAuth();

        var lockedOut = LockoutResult(credential, now);
        if (lockedOut is not null)
            return lockedOut;

        if (!string.IsNullOrEmpty(pin) && _hasher.Verify(pin, ToSecretHash(
                credential.PinHash, credential.PinSalt, credential.PinIterations)))
        {
            credential.RegisterSuccessfulAuthentication(now);
            await _credentials.UpdateAsync(credential, cancellationToken);
            await WriteAuditAsync(
                AuditEventType.ParentLogin,
                "Parent authenticated with PIN.",
                now,
                metadataJson: null,
                cancellationToken);

            return new ParentAuthResult(
                ParentAuthStatus.Success,
                ParentCredential.MaxFailedAttempts,
                LockoutUntilUtc: null);
        }

        return await RegisterAuthenticationFailureAsync(credential, "PIN", now, cancellationToken);
    }

    public async Task<ParentAuthResult> ChangePinAsync(
        string currentPin,
        string newPin,
        CancellationToken cancellationToken = default)
    {
        ParentPinPolicy.EnsureValid(newPin);

        var now = UtcNow();
        var credential = await _credentials.GetAsync(cancellationToken);
        if (credential is null)
            return NotConfiguredAuth();

        var lockedOut = LockoutResult(credential, now);
        if (lockedOut is not null)
            return lockedOut;

        if (string.IsNullOrEmpty(currentPin) || !_hasher.Verify(currentPin, ToSecretHash(
                credential.PinHash, credential.PinSalt, credential.PinIterations)))
        {
            return await RegisterAuthenticationFailureAsync(credential, "PIN", now, cancellationToken);
        }

        var pinHash = _hasher.Hash(newPin);
        credential.ChangePin(pinHash.HashBase64, pinHash.SaltBase64, pinHash.Iterations, now);

        await _credentials.UpdateAsync(credential, cancellationToken);
        await WriteAuditAsync(
            AuditEventType.PinChanged,
            "Parent PIN changed.",
            now,
            metadataJson: null,
            cancellationToken);

        return new ParentAuthResult(
            ParentAuthStatus.Success,
            ParentCredential.MaxFailedAttempts,
            LockoutUntilUtc: null);
    }

    public async Task<ParentPinResetResult> ResetPinWithRecoveryCodeAsync(
        string recoveryCode,
        string newPin,
        CancellationToken cancellationToken = default)
    {
        ParentPinPolicy.EnsureValid(newPin);

        var now = UtcNow();
        var credential = await _credentials.GetAsync(cancellationToken);
        if (credential is null)
            return NotConfiguredReset();

        var lockedOut = LockoutResult(credential, now);
        if (lockedOut is not null)
        {
            return new ParentPinResetResult(
                lockedOut.Status,
                lockedOut.RemainingAttempts,
                lockedOut.LockoutUntilUtc,
                RecoveryCode: null);
        }

        var normalizedCode = ParentRecoveryCode.Normalize(recoveryCode);
        if (normalizedCode.Length == 0 || !_hasher.Verify(normalizedCode, ToSecretHash(
                credential.RecoveryCodeHash,
                credential.RecoveryCodeSalt,
                credential.RecoveryCodeIterations)))
        {
            var failure = await RegisterAuthenticationFailureAsync(
                credential,
                "recovery code",
                now,
                cancellationToken);

            return new ParentPinResetResult(
                failure.Status,
                failure.RemainingAttempts,
                failure.LockoutUntilUtc,
                RecoveryCode: null);
        }

        var pinHash = _hasher.Hash(newPin);
        var replacementCode = _recoveryCodes.Generate();
        var recoveryHash = _hasher.Hash(ParentRecoveryCode.Normalize(replacementCode));

        credential.ChangePin(pinHash.HashBase64, pinHash.SaltBase64, pinHash.Iterations, now);
        credential.ChangeRecoveryCode(
            recoveryHash.HashBase64,
            recoveryHash.SaltBase64,
            recoveryHash.Iterations,
            now);

        await _credentials.UpdateAsync(credential, cancellationToken);
        await WriteAuditAsync(
            AuditEventType.RecoveryCodeUsed,
            "Parent PIN reset with recovery code.",
            now,
            metadataJson: null,
            cancellationToken);
        await WriteAuditAsync(
            AuditEventType.PinChanged,
            "Parent PIN changed after recovery reset.",
            now,
            metadataJson: null,
            cancellationToken);

        return new ParentPinResetResult(
            ParentAuthStatus.Success,
            ParentCredential.MaxFailedAttempts,
            LockoutUntilUtc: null,
            replacementCode);
    }

    public async Task<ParentRecoveryCodeResult> RegenerateRecoveryCodeAsync(
        string pin,
        CancellationToken cancellationToken = default)
    {
        var now = UtcNow();
        var credential = await _credentials.GetAsync(cancellationToken);
        if (credential is null)
            return NotConfiguredRecoveryCode();

        var lockedOut = LockoutResult(credential, now);
        if (lockedOut is not null)
        {
            return new ParentRecoveryCodeResult(
                lockedOut.Status,
                lockedOut.RemainingAttempts,
                lockedOut.LockoutUntilUtc,
                RecoveryCode: null);
        }

        if (string.IsNullOrEmpty(pin) || !_hasher.Verify(pin, ToSecretHash(
                credential.PinHash, credential.PinSalt, credential.PinIterations)))
        {
            var failure = await RegisterAuthenticationFailureAsync(credential, "PIN", now, cancellationToken);

            return new ParentRecoveryCodeResult(
                failure.Status,
                failure.RemainingAttempts,
                failure.LockoutUntilUtc,
                RecoveryCode: null);
        }

        var replacementCode = _recoveryCodes.Generate();
        var recoveryHash = _hasher.Hash(ParentRecoveryCode.Normalize(replacementCode));

        credential.RegisterSuccessfulAuthentication(now);
        credential.ChangeRecoveryCode(
            recoveryHash.HashBase64,
            recoveryHash.SaltBase64,
            recoveryHash.Iterations,
            now);

        await _credentials.UpdateAsync(credential, cancellationToken);
        await WriteAuditAsync(
            AuditEventType.RecoveryCodeRegenerated,
            "Parent recovery code regenerated after PIN verification.",
            now,
            metadataJson: null,
            cancellationToken);

        return new ParentRecoveryCodeResult(
            ParentAuthStatus.Success,
            ParentCredential.MaxFailedAttempts,
            LockoutUntilUtc: null,
            replacementCode);
    }

    private async Task<ParentAuthResult> RegisterAuthenticationFailureAsync(
        ParentCredential credential,
        string secretKind,
        DateTime now,
        CancellationToken cancellationToken)
    {
        credential.RegisterFailedAttempt(now);
        await _credentials.UpdateAsync(credential, cancellationToken);

        var lockedOut = credential.IsLockedOut(now);
        var remaining = lockedOut
            ? 0
            : Math.Max(0, ParentCredential.MaxFailedAttempts - credential.FailedAttempts);

        await WriteAuditAsync(
            AuditEventType.AuthenticationFailed,
            $"Parent {secretKind} authentication failed.",
            now,
            JsonSerializer.Serialize(new { remainingAttempts = remaining }),
            cancellationToken);

        if (lockedOut)
        {
            await WriteAuditAsync(
                AuditEventType.LockoutTriggered,
                "Parent authentication locked after repeated failures.",
                now,
                JsonSerializer.Serialize(new { lockoutUntilUtc = credential.LockoutUntilUtc }),
                cancellationToken);
        }

        return new ParentAuthResult(
            lockedOut ? ParentAuthStatus.LockedOut : ParentAuthStatus.InvalidCredential,
            remaining,
            lockedOut ? credential.LockoutUntilUtc : null);
    }

    private static ParentAuthResult? LockoutResult(ParentCredential credential, DateTime now)
    {
        if (!credential.IsLockedOut(now))
            return null;

        return new ParentAuthResult(ParentAuthStatus.LockedOut, 0, credential.LockoutUntilUtc);
    }

    private static SecretHash ToSecretHash(string hash, string salt, int iterations) =>
        new(hash, salt, iterations);

    private static ParentAuthResult NotConfiguredAuth() =>
        new(ParentAuthStatus.NotConfigured, 0, LockoutUntilUtc: null);

    private static ParentPinResetResult NotConfiguredReset() =>
        new(ParentAuthStatus.NotConfigured, 0, LockoutUntilUtc: null, RecoveryCode: null);

    private static ParentRecoveryCodeResult NotConfiguredRecoveryCode() =>
        new(ParentAuthStatus.NotConfigured, 0, LockoutUntilUtc: null, RecoveryCode: null);

    private async Task WriteAuditAsync(
        AuditEventType eventType,
        string description,
        DateTime now,
        string? metadataJson,
        CancellationToken cancellationToken)
    {
        var auditLog = AuditLog.Record(
            AuditActor.Parent,
            eventType,
            description,
            now,
            metadataJson,
            _correlation.Current);

        await _auditLogs.AddAsync(auditLog, cancellationToken);
    }

    private DateTime UtcNow() => _timeProvider.GetUtcNow().UtcDateTime;
}
