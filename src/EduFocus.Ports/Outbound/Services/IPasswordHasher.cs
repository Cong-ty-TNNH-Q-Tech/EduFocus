namespace EduFocus.Ports.Outbound.Services;

/// <summary>Stored PBKDF2 parameters. No plaintext secret is part of this value.</summary>
public sealed record SecretHash(string HashBase64, string SaltBase64, int Iterations);

/// <summary>
/// Hashes and verifies PIN/recovery-code secrets with PBKDF2-SHA256,
/// a fresh random salt and constant-time comparison (system architecture §9.1).
/// </summary>
public interface IPasswordHasher
{
    SecretHash Hash(string secret);

    bool Verify(string secret, SecretHash stored);
}
