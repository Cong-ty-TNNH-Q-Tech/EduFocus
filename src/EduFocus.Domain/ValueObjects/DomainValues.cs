using System.Security.Cryptography;

namespace EduFocus.Domain.ValueObjects;

/// <summary>Opaque PBKDF2 output only. Callers must never pass the PIN or recovery code here.</summary>
public sealed class CredentialHash
{
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
