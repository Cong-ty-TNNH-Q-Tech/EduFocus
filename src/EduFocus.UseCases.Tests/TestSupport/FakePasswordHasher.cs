using EduFocus.Ports.Outbound.Services;

namespace EduFocus.UseCases.Tests.TestSupport;

/// <summary>
/// Deterministic stand-in for PBKDF2 so use-case tests stay fast. It still
/// produces valid 32-byte hashes and 16-byte salts because the domain entity
/// validates their shape.
/// </summary>
internal sealed class FakePasswordHasher : IPasswordHasher
{
    private const string FixedSalt = "AAAAAAAAAAAAAAAAAAAAAA==";
    private readonly Dictionary<string, string> _secretsByHash = new(StringComparer.Ordinal);
    private int _nextHash;

    public int HashCount { get; private set; }

    public SecretHash Hash(string secret)
    {
        HashCount++;

        var hash = Encode(_nextHash++);
        _secretsByHash[hash] = secret;
        return new SecretHash(hash, FixedSalt, 600_000);
    }

    public bool Verify(string secret, SecretHash stored) =>
        _secretsByHash.TryGetValue(stored.HashBase64, out var known) && known == secret;

    private static string Encode(int value)
    {
        var bytes = new byte[32];
        BitConverter.GetBytes(value).CopyTo(bytes, 0);
        return Convert.ToBase64String(bytes);
    }
}
