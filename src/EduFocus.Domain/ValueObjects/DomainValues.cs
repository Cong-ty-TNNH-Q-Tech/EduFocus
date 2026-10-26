using System.Security.Cryptography;
using System.Text;

namespace EduFocus.Domain.ValueObjects;

/// <summary>Opaque PBKDF2 output only. Callers must never pass the PIN or recovery code here.</summary>
public sealed class CredentialHash
{
    public const int DefaultIterations = 600_000;
    public string HashBase64 { get; private set; }
    public string SaltBase64 { get; private set; }
    public int Iterations { get; private set; }
    private CredentialHash() { HashBase64 = SaltBase64 = string.Empty; }
    private CredentialHash(string hash,string salt,int iterations) { HashBase64=hash;SaltBase64=salt;Iterations=iterations; }
    public static CredentialHash FromPbkdf2(string hashBase64,string saltBase64,int iterations)
    {
        if(iterations<600_000)throw new ArgumentOutOfRangeException(nameof(iterations),"PBKDF2 must use at least 600,000 iterations.");
        byte[] hash,salt;try{hash=Convert.FromBase64String(hashBase64);salt=Convert.FromBase64String(saltBase64);}catch(FormatException ex){throw new ArgumentException("Hash and salt must be base64 encoded.",ex);}
        if(hash.Length!=32)throw new ArgumentException("Credential hash must be 32 bytes.",nameof(hashBase64));if(salt.Length!=16)throw new ArgumentException("Credential salt must be 16 bytes.",nameof(saltBase64));
        return new(Convert.ToBase64String(hash),Convert.ToBase64String(salt),iterations);
    }
    public static CredentialHash HashSecret(string secret, int iterations = DefaultIterations)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(secret);
        if (iterations < DefaultIterations) throw new ArgumentOutOfRangeException(nameof(iterations), "PBKDF2 must use at least 600,000 iterations.");
        var salt = RandomNumberGenerator.GetBytes(16);
        var secretBytes = Encoding.UTF8.GetBytes(secret);
        try
        {
            var hash = Rfc2898DeriveBytes.Pbkdf2(secretBytes, salt, iterations, HashAlgorithmName.SHA256, 32);
            try { return FromPbkdf2(Convert.ToBase64String(hash), Convert.ToBase64String(salt), iterations); }
            finally { CryptographicOperations.ZeroMemory(hash); }
        }
        finally { CryptographicOperations.ZeroMemory(secretBytes); CryptographicOperations.ZeroMemory(salt); }
    }
    public bool VerifySecret(string secret)
    {
        ArgumentNullException.ThrowIfNull(secret);
        if (secret.Length > 128) throw new ArgumentOutOfRangeException(nameof(secret), "Credential input is too long.");
        var expected = Convert.FromBase64String(HashBase64);
        var salt = Convert.FromBase64String(SaltBase64);
        var secretBytes = Encoding.UTF8.GetBytes(secret);
        try
        {
            var actual = Rfc2898DeriveBytes.Pbkdf2(secretBytes, salt, Iterations, HashAlgorithmName.SHA256, expected.Length);
            try { return CryptographicOperations.FixedTimeEquals(actual, expected); }
            finally { CryptographicOperations.ZeroMemory(actual); }
        }
        finally { CryptographicOperations.ZeroMemory(secretBytes); CryptographicOperations.ZeroMemory(salt); CryptographicOperations.ZeroMemory(expected); }
    }
    public bool SameHash(CredentialHash other)=>CryptographicOperations.FixedTimeEquals(Convert.FromBase64String(HashBase64),Convert.FromBase64String(other.HashBase64)) && CryptographicOperations.FixedTimeEquals(Convert.FromBase64String(SaltBase64),Convert.FromBase64String(other.SaltBase64));
}

internal static class Utc
{
    public static DateTimeOffset Require(DateTimeOffset value)
    {
        if(value.Offset!=TimeSpan.Zero)throw new ArgumentException("Timestamps must be UTC (offset +00:00).",nameof(value));
        return value;
    }
}
