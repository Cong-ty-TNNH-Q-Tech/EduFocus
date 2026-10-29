using EduFocus.Infrastructure.Auth;
using EduFocus.Ports.Outbound.Services;
using FluentAssertions;

namespace EduFocus.Infrastructure.Tests;

public sealed class Pbkdf2PasswordHasherTests
{
    private readonly Pbkdf2PasswordHasher _hasher = new();

    [Fact]
    public void Hash_produces_pbkdf2_sha256_parameters()
    {
        var stored = _hasher.Hash("482915");

        Convert.FromBase64String(stored.HashBase64).Should().HaveCount(Pbkdf2PasswordHasher.HashSizeBytes);
        Convert.FromBase64String(stored.SaltBase64).Should().HaveCount(Pbkdf2PasswordHasher.SaltSizeBytes);
        stored.Iterations.Should().Be(600_000);
    }

    [Fact]
    public void Hash_uses_a_fresh_salt_for_every_call()
    {
        var first = _hasher.Hash("482915");
        var second = _hasher.Hash("482915");

        first.SaltBase64.Should().NotBe(second.SaltBase64);
        first.HashBase64.Should().NotBe(second.HashBase64);
    }

    [Fact]
    public void Verify_accepts_matching_secret_and_rejects_others()
    {
        var stored = _hasher.Hash("482915");

        _hasher.Verify("482915", stored).Should().BeTrue();
        _hasher.Verify("482916", stored).Should().BeFalse();
        _hasher.Verify(string.Empty, stored).Should().BeFalse();
    }

    [Fact]
    public void Verify_uses_the_stored_salt_and_iterations()
    {
        var stored = _hasher.Hash("482915");
        var withDifferentSalt = stored with { SaltBase64 = _hasher.Hash("482915").SaltBase64 };

        _hasher.Verify("482915", withDifferentSalt).Should().BeFalse();
    }

    [Theory]
    [InlineData("not base64!", "AAAAAAAAAAAAAAAAAAAAAA==")]
    [InlineData("AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA=", "not base64!")]
    [InlineData("AAAA", "AAAAAAAAAAAAAAAAAAAAAA==")]
    [InlineData("AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA=", "AAAA")]
    public void Verify_rejects_malformed_stored_parameters(string hash, string salt)
    {
        var stored = new SecretHash(hash, salt, 600_000);

        _hasher.Verify("482915", stored).Should().BeFalse();
    }

    [Fact]
    public void Verify_rejects_weaker_iteration_counts()
    {
        var stored = _hasher.Hash("482915") with { Iterations = 599_999 };

        _hasher.Verify("482915", stored).Should().BeFalse();
    }

    [Fact]
    public void Hash_requires_a_secret()
    {
        var act = () => _hasher.Hash(string.Empty);

        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void Verify_works_with_non_pin_secrets()
    {
        const string secret = "NPQR23456789";
        var stored = _hasher.Hash(secret);

        _hasher.Verify(secret, stored).Should().BeTrue();
        _hasher.Verify(secret.ToLowerInvariant(), stored).Should().BeFalse();
    }
}
