using System.Security.Cryptography;
using EduFocus.Domain.Entities;
using EduFocus.Domain.Enums;
using EduFocus.Domain.ValueObjects;
using Microsoft.EntityFrameworkCore;

namespace EduFocus.Infrastructure.Persistence;

public enum CredentialOperationStatus { Succeeded, InvalidCredential, LockedOut }
public sealed record CredentialLockoutStatus(int FailedAttempts, DateTimeOffset? LockoutUntilUtc, TimeSpan Remaining)
{
    public bool IsLockedOut => Remaining > TimeSpan.Zero;
}

/// <summary>Recovery code is returned only by creation or rotation; callers must present it once and never persist or log it.</summary>
public sealed record CredentialOperationResult(CredentialOperationStatus Status, string? RecoveryCode = null);

/// <summary>Coordinates credential hashing, lockout persistence and credential-change audit events.</summary>
public sealed class ParentCredentialService(AppDbContext db)
{
    private const string RecoveryAlphabet = "ABCDEFGHJKLMNPQRSTUVWXYZ23456789";
    private static readonly string[] WeakPins = ["000000", "111111", "123456", "654321", "123123", "121212", "112233", "1234567", "12345678", "87654321"];
    private static readonly SemaphoreSlim ProcessGate = new(1, 1);

    public async Task<CredentialOperationResult> SetUpAsync(string pin, DateTimeOffset now, CancellationToken cancellationToken = default)
    {
        ValidateNewPin(pin);
        now = RequireUtc(now);
        await ProcessGate.WaitAsync(cancellationToken);
        try
        {
            await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
            if (await db.ParentCredentials.AnyAsync(cancellationToken)) throw new InvalidOperationException("Parent credentials are already configured.");
            var recovery = GenerateRecoveryCode();
            db.ParentCredentials.Add(ParentCredentials.Create(CredentialHash.HashSecret(pin), CredentialHash.HashSecret(recovery), now));
            db.AuditLogs.Add(AuditLog.Create(now, AuditActor.Parent, "CredentialCreated", "Parent credentials were created.", now));
            await db.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return new(CredentialOperationStatus.Succeeded, recovery);
        }
        finally { ProcessGate.Release(); }
    }

    public Task<CredentialOperationResult> AuthenticatePinAsync(string pin, DateTimeOffset now, CancellationToken cancellationToken = default) =>
        AuthenticateAsync(pin, now, cancellationToken);

    public async Task<CredentialLockoutStatus> GetLockoutStatusAsync(DateTimeOffset now, CancellationToken cancellationToken = default)
    {
        now = RequireUtc(now);
        var credentials = await GetCredentialsAsync(cancellationToken);
        var remaining = credentials.LockoutUntilUtc is { } until && until > now ? until - now : TimeSpan.Zero;
        return new(credentials.FailedAttempts, credentials.LockoutUntilUtc, remaining);
    }

    public async Task<CredentialOperationResult> ChangePinAsync(string currentPin, string newPin, DateTimeOffset now, CancellationToken cancellationToken = default)
    {
        ValidateNewPin(newPin);
        now = RequireUtc(now);
        await ProcessGate.WaitAsync(cancellationToken);
        try
        {
            await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
            var credentials = await GetCredentialsAsync(cancellationToken);
            if (credentials.IsLockedOut(now)) return new(CredentialOperationStatus.LockedOut);
            if (!credentials.Pin.VerifySecret(currentPin))
            {
                RecordFailure(credentials, now);
                await db.SaveChangesAsync(cancellationToken);
                await transaction.CommitAsync(cancellationToken);
                return new(CredentialOperationStatus.InvalidCredential);
            }
            if (credentials.Pin.VerifySecret(newPin)) throw new ArgumentException("The new PIN must differ from the current PIN.", nameof(newPin));
            credentials.RecordSuccessfulAuthentication(now);
            credentials.ChangePin(CredentialHash.HashSecret(newPin), now);
            db.AuditLogs.Add(AuditLog.Create(now, AuditActor.Parent, "PinChanged", "Parent PIN was changed.", now));
            await db.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return new(CredentialOperationStatus.Succeeded);
        }
        finally { ProcessGate.Release(); }
    }

    public async Task<CredentialOperationResult> RecoverPinAsync(string recoveryCode, string newPin, DateTimeOffset now, CancellationToken cancellationToken = default)
    {
        ValidateNewPin(newPin);
        now = RequireUtc(now);
        await ProcessGate.WaitAsync(cancellationToken);
        try
        {
            await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
            var credentials = await GetCredentialsAsync(cancellationToken);
            if (credentials.IsLockedOut(now)) return new(CredentialOperationStatus.LockedOut);
            if (!credentials.RecoveryCode.VerifySecret(recoveryCode))
            {
                RecordFailure(credentials, now);
                await db.SaveChangesAsync(cancellationToken);
                await transaction.CommitAsync(cancellationToken);
                return new(CredentialOperationStatus.InvalidCredential);
            }
            if (credentials.Pin.VerifySecret(newPin)) throw new ArgumentException("The new PIN must differ from the current PIN.", nameof(newPin));
            var replacementCode = GenerateRecoveryCode();
            credentials.RecordSuccessfulAuthentication(now);
            credentials.ChangePin(CredentialHash.HashSecret(newPin), now);
            credentials.ChangeRecoveryCode(CredentialHash.HashSecret(replacementCode));
            db.AuditLogs.Add(AuditLog.Create(now, AuditActor.Parent, "PinRecovered", "Parent PIN was recovered using the recovery code.", now));
            await db.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return new(CredentialOperationStatus.Succeeded, replacementCode);
        }
        finally { ProcessGate.Release(); }
    }

    public async Task<CredentialOperationResult> RegenerateRecoveryCodeAsync(string pin, DateTimeOffset now, CancellationToken cancellationToken = default)
    {
        now = RequireUtc(now);
        await ProcessGate.WaitAsync(cancellationToken);
        try
        {
            await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
            var credentials = await GetCredentialsAsync(cancellationToken);
            if (credentials.IsLockedOut(now)) return new(CredentialOperationStatus.LockedOut);
            if (!credentials.Pin.VerifySecret(pin))
            {
                RecordFailure(credentials, now);
                await db.SaveChangesAsync(cancellationToken);
                await transaction.CommitAsync(cancellationToken);
                return new(CredentialOperationStatus.InvalidCredential);
            }
            var recovery = GenerateRecoveryCode();
            credentials.RecordSuccessfulAuthentication(now);
            credentials.ChangeRecoveryCode(CredentialHash.HashSecret(recovery));
            db.AuditLogs.Add(AuditLog.Create(now, AuditActor.Parent, "RecoveryCodeRegenerated", "Parent recovery code was regenerated.", now));
            await db.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return new(CredentialOperationStatus.Succeeded, recovery);
        }
        finally { ProcessGate.Release(); }
    }

    private async Task<CredentialOperationResult> AuthenticateAsync(string secret, DateTimeOffset now, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(secret);
        now = RequireUtc(now);
        await ProcessGate.WaitAsync(cancellationToken);
        try
        {
            await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
            var credentials = await GetCredentialsAsync(cancellationToken);
            if (credentials.IsLockedOut(now)) return new(CredentialOperationStatus.LockedOut);
            var valid = credentials.Pin.VerifySecret(secret);
            if (valid)
            {
                credentials.RecordSuccessfulAuthentication(now);
                db.AuditLogs.Add(AuditLog.Create(now, AuditActor.Parent, "ParentLogin", "Parent Mode login succeeded.", now));
            }
            else RecordFailure(credentials, now);
            await db.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return new(valid ? CredentialOperationStatus.Succeeded : CredentialOperationStatus.InvalidCredential);
        }
        finally { ProcessGate.Release(); }
    }

    private async Task<ParentCredentials> GetCredentialsAsync(CancellationToken cancellationToken) =>
        await db.ParentCredentials.SingleOrDefaultAsync(cancellationToken)
        ?? throw new InvalidOperationException("Parent credentials have not been configured.");

    private void RecordFailure(ParentCredentials credentials, DateTimeOffset now)
    {
        credentials.RecordFailedAttempt(now);
        db.AuditLogs.Add(AuditLog.Create(now, AuditActor.Parent, "CredentialAuthenticationFailed", "A parent credential authentication attempt failed.", now));
    }

    private static void ValidateNewPin(string pin)
    {
        if (pin is null || pin.Length is < 6 or > 8 || pin.Any(c => c is < '0' or > '9') || WeakPins.Contains(pin, StringComparer.Ordinal))
            throw new ArgumentException("PIN must be a non-common sequence of 6 to 8 digits.", nameof(pin));
    }

    private static string GenerateRecoveryCode()
    {
        Span<char> code = stackalloc char[16];
        for (var i = 0; i < code.Length; i++) code[i] = RecoveryAlphabet[RandomNumberGenerator.GetInt32(RecoveryAlphabet.Length)];
        return new string(code);
    }

    private static DateTimeOffset RequireUtc(DateTimeOffset now) => now.Offset == TimeSpan.Zero ? now : throw new ArgumentException("Timestamp must be UTC.", nameof(now));
}
