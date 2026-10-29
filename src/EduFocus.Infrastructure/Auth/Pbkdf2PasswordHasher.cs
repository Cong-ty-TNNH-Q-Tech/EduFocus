using System.Security.Cryptography;
using EduFocus.Ports.Outbound.Services;

namespace EduFocus.Infrastructure.Auth;

/// <summary>
/// PBKDF2-SHA256 with a fresh 16-byte salt per secret and constant-time
/// comparison, as required by system architecture §9.1.
/// </summary>
public sealed class Pbkdf2PasswordHasher : IPasswordHasher
{
    public const int DefaultIterations = 600_000;
    public const int SaltSizeBytes = 16;
    public const int HashSizeBytes = 32;

    private static readonly HashAlgorithmName Algorithm = HashAlgorithmName.SHA256;

    public SecretHash Hash(string secret)
    {
        ArgumentException.ThrowIfNullOrEmpty(secret);

        var salt = RandomNumberGenerator.GetBytes(SaltSizeBytes);
        var hash = Rfc2898DeriveBytes.Pbkdf2(secret, salt, DefaultIterations, Algorithm, HashSizeBytes);

        return new SecretHash(
            Convert.ToBase64String(hash),
            Convert.ToBase64String(salt),
            DefaultIterations);
    }

    public bool Verify(string secret, SecretHash stored)
    {
        ArgumentNullException.ThrowIfNull(stored);

        if (string.IsNullOrEmpty(secret) || stored.Iterations < DefaultIterations)
            return false;

        if (string.IsNullOrEmpty(stored.HashBase64) || string.IsNullOrEmpty(stored.SaltBase64))
            return false;

        byte[] hash;
        byte[] salt;
        try
        {
            hash = Convert.FromBase64String(stored.HashBase64);
            salt = Convert.FromBase64String(stored.SaltBase64);
        }
        catch (FormatException)
        {
            return false;
        }

        if (hash.Length != HashSizeBytes || salt.Length != SaltSizeBytes)
            return false;

        var candidate = Rfc2898DeriveBytes.Pbkdf2(secret, salt, stored.Iterations, Algorithm, HashSizeBytes);
        return CryptographicOperations.FixedTimeEquals(candidate, hash);
    }
}
