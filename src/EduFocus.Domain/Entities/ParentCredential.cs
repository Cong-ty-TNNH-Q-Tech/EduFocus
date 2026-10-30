using EduFocus.Domain.Common;
using EduFocus.Domain.ValueObjects;

namespace EduFocus.Domain.Entities;

/// <summary>
/// Parent PIN and recovery-code hashes. Only Base64 PBKDF2 output is stored;
/// plaintext secrets never enter the domain model (BR-01, BR-02, BR-03).
/// </summary>
public sealed class ParentCredential
{
    public const int MinimumIterations = 600_000;
    public const int MaxFailedAttempts = 5;
    public static readonly TimeSpan LockoutDuration = TimeSpan.FromMinutes(15);

    private const int HashBytes = 32;
    private const int SaltBytes = 16;

    private ParentCredential(
        ParentCredentialId id,
        string pinHash,
        string pinSalt,
        int pinIterations,
        string recoveryCodeHash,
        string recoveryCodeSalt,
        int recoveryCodeIterations,
        DateTime createdAtUtc)
    {
        Id = id;
        PinHash = pinHash;
        PinSalt = pinSalt;
        PinIterations = pinIterations;
        RecoveryCodeHash = recoveryCodeHash;
        RecoveryCodeSalt = recoveryCodeSalt;
        RecoveryCodeIterations = recoveryCodeIterations;
        FailedAttempts = 0;
        LockoutUntilUtc = null;
        PinUpdatedAtUtc = createdAtUtc;
        CreatedAtUtc = createdAtUtc;
    }

    public ParentCredentialId Id { get; }

    public string PinHash { get; private set; }

    public string PinSalt { get; private set; }

    public int PinIterations { get; private set; }

    public string RecoveryCodeHash { get; private set; }

    public string RecoveryCodeSalt { get; private set; }

    public int RecoveryCodeIterations { get; private set; }

    /// <summary>Shared counter for PIN and recovery-code failures.</summary>
    public int FailedAttempts { get; private set; }

    public DateTime? LockoutUntilUtc { get; private set; }

    public DateTime PinUpdatedAtUtc { get; private set; }

    public DateTime CreatedAtUtc { get; }

    public static ParentCredential Create(
        string pinHash,
        string pinSalt,
        int pinIterations,
        string recoveryCodeHash,
        string recoveryCodeSalt,
        int recoveryCodeIterations,
        DateTime nowUtc)
    {
        Guard.Utc(nowUtc, nameof(nowUtc));
        ValidateHash(pinHash, nameof(pinHash));
        ValidateSalt(pinSalt, nameof(pinSalt));
        ValidateIterations(pinIterations, nameof(pinIterations));
        ValidateHash(recoveryCodeHash, nameof(recoveryCodeHash));
        ValidateSalt(recoveryCodeSalt, nameof(recoveryCodeSalt));
        ValidateIterations(recoveryCodeIterations, nameof(recoveryCodeIterations));

        return new ParentCredential(
            ParentCredentialId.New(),
            pinHash,
            pinSalt,
            pinIterations,
            recoveryCodeHash,
            recoveryCodeSalt,
            recoveryCodeIterations,
            nowUtc);
    }

    public bool IsLockedOut(DateTime nowUtc)
    {
        Guard.Utc(nowUtc, nameof(nowUtc));
        return LockoutUntilUtc is { } until && until > nowUtc;
    }

    /// <summary>Counts a wrong PIN or recovery code; the fifth failure locks for 15 minutes.</summary>
    public void RegisterFailedAttempt(DateTime nowUtc)
    {
        Guard.Utc(nowUtc, nameof(nowUtc));

        if (IsLockedOut(nowUtc))
            throw new InvalidOperationException("Credential is locked; wait for the lockout to expire.");

        if (LockoutUntilUtc is not null)
        {
            FailedAttempts = 0;
            LockoutUntilUtc = null;
        }

        FailedAttempts++;

        if (FailedAttempts >= MaxFailedAttempts)
            LockoutUntilUtc = nowUtc.Add(LockoutDuration);
    }

    public void RegisterSuccessfulAuthentication(DateTime nowUtc)
    {
        Guard.Utc(nowUtc, nameof(nowUtc));

        FailedAttempts = 0;
        LockoutUntilUtc = null;
    }

    public void ChangePin(string pinHash, string pinSalt, int pinIterations, DateTime nowUtc)
    {
        Guard.Utc(nowUtc, nameof(nowUtc));
        ValidateHash(pinHash, nameof(pinHash));
        ValidateSalt(pinSalt, nameof(pinSalt));
        ValidateIterations(pinIterations, nameof(pinIterations));

        PinHash = pinHash;
        PinSalt = pinSalt;
        PinIterations = pinIterations;
        PinUpdatedAtUtc = nowUtc;
        FailedAttempts = 0;
        LockoutUntilUtc = null;
    }

    public void ChangeRecoveryCode(
        string recoveryCodeHash,
        string recoveryCodeSalt,
        int recoveryCodeIterations,
        DateTime nowUtc)
    {
        Guard.Utc(nowUtc, nameof(nowUtc));
        ValidateHash(recoveryCodeHash, nameof(recoveryCodeHash));
        ValidateSalt(recoveryCodeSalt, nameof(recoveryCodeSalt));
        ValidateIterations(recoveryCodeIterations, nameof(recoveryCodeIterations));

        RecoveryCodeHash = recoveryCodeHash;
        RecoveryCodeSalt = recoveryCodeSalt;
        RecoveryCodeIterations = recoveryCodeIterations;
    }

    private static void ValidateHash(string hash, string parameterName)
    {
        Guard.NotNullOrWhiteSpace(hash, parameterName);
        ValidateBase64(hash, HashBytes, parameterName);
    }

    private static void ValidateSalt(string salt, string parameterName)
    {
        Guard.NotNullOrWhiteSpace(salt, parameterName);
        ValidateBase64(salt, SaltBytes, parameterName);
    }

    private static void ValidateIterations(int iterations, string parameterName)
    {
        if (iterations < MinimumIterations)
            throw new ArgumentOutOfRangeException(parameterName, iterations, $"PBKDF2 requires at least {MinimumIterations} iterations.");
    }

    private static void ValidateBase64(string value, int expectedBytes, string parameterName)
    {
        byte[] bytes;
        try
        {
            bytes = Convert.FromBase64String(value);
        }
        catch (FormatException)
        {
            throw new ArgumentException("Value must be valid Base64.", parameterName);
        }

        if (bytes.Length != expectedBytes)
            throw new ArgumentException($"Value must decode to {expectedBytes} bytes.", parameterName);
    }
}
