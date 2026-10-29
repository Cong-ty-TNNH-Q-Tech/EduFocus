namespace EduFocus.Ports.DTOs;

public enum ParentAuthStatus
{
    Success,
    NotConfigured,
    InvalidCredential,
    LockedOut
}

/// <summary>Result of a PIN login or PIN change. Never contains the secret.</summary>
public sealed record ParentAuthResult(
    ParentAuthStatus Status,
    int RemainingAttempts,
    DateTime? LockoutUntilUtc);

/// <summary>
/// Result of resetting a PIN with a recovery code. The new recovery code is
/// returned exactly once and is not retrievable later.
/// </summary>
public sealed record ParentPinResetResult(
    ParentAuthStatus Status,
    int RemainingAttempts,
    DateTime? LockoutUntilUtc,
    string? RecoveryCode);

/// <summary>Returned once after setup so the parent can store the recovery code.</summary>
public sealed record ParentCredentialSetupResult(string RecoveryCode);
