using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using EduFocus.Infrastructure.Protection.Certificates;
using FluentAssertions;

namespace EduFocus.Infrastructure.Tests;

public sealed class CertificateAuthorityTests : IDisposable
{
    private readonly string _directory = Path.Combine(
        Path.GetTempPath(),
        "edufocus-ca-tests",
        Guid.NewGuid().ToString("N"));

    private readonly InMemoryTrustStore _trustStore = new();
    private readonly TestLogger _logger = new();

    [Fact]
    public async Task EnsureTrusted_installs_only_the_public_root()
    {
        using var authority = CreateAuthority();

        var result = await authority.EnsureTrustedAsync();

        result.Ready.Should().BeTrue();
        result.Warning.Should().BeNull();
        authority.IsReady.Should().BeTrue();

        _trustStore.Certificates.Should().ContainSingle();
        var installed = _trustStore.Certificates.Values.Single();
        installed.Subject.Should().Contain("EduFocus Local Blocking CA");
        installed.HasPrivateKey.Should().BeFalse();
        installed.Extensions
            .OfType<X509BasicConstraintsExtension>()
            .Should().Contain(extension => extension.CertificateAuthority);
    }

    [Fact]
    public async Task Root_is_reused_across_authority_instances()
    {
        string thumbprint;

        using (var first = CreateAuthority())
        {
            await first.EnsureTrustedAsync();
            thumbprint = _trustStore.Certificates.Keys.Single();
        }

        using var second = CreateAuthority();
        var result = await second.EnsureTrustedAsync();

        result.Ready.Should().BeTrue();
        _trustStore.Certificates.Keys.Should().Equal(thumbprint);
    }

    [Fact]
    public async Task Root_file_is_dpapi_protected_and_contains_no_plaintext_key()
    {
        using var authority = CreateAuthority();
        await authority.EnsureTrustedAsync();

        var rootPath = Path.Combine(_directory, CertificateAuthorityOptions.RootFileName);
        var bytes = File.ReadAllBytes(rootPath);

        Encoding.ASCII.GetString(bytes, 0, 6).Should().Be("EDUCA1");
        bytes.Length.Should().BeGreaterThan(6);
        Encoding.ASCII.GetString(bytes).Should().NotContain("PRIVATE KEY");
        Encoding.ASCII.GetString(bytes).Should().NotContain("BEGIN");
    }

    [Fact]
    public async Task Leaf_has_san_server_auth_and_chains_to_the_root()
    {
        using var authority = CreateAuthority();
        await authority.EnsureTrustedAsync();

        var leaf = authority.GetServerCertificate("ChatGPT.com.");

        leaf.Should().NotBeNull();
        leaf!.HasPrivateKey.Should().BeTrue();
        leaf.Subject.Should().Contain("chatgpt.com");
        leaf.Extensions.OfType<X509BasicConstraintsExtension>()
            .Should().Contain(extension => !extension.CertificateAuthority);

        var san = leaf.Extensions.OfType<X509SubjectAlternativeNameExtension>().Single();
        san.EnumerateDnsNames().Should().Contain("chatgpt.com");

        var usages = leaf.Extensions.OfType<X509EnhancedKeyUsageExtension>().Single();
        usages.EnhancedKeyUsages.Cast<Oid>().Select(oid => oid.Value)
            .Should().Contain("1.3.6.1.5.5.7.3.1");

        var root = _trustStore.Certificates.Values.Single();
        using var chain = new X509Chain();
        chain.ChainPolicy.TrustMode = X509ChainTrustMode.CustomRootTrust;
        chain.ChainPolicy.CustomTrustStore.Add(root);
        chain.ChainPolicy.RevocationMode = X509RevocationMode.NoCheck;

        chain.Build(leaf).Should().BeTrue();
    }

    [Fact]
    public async Task Leaf_certificates_are_cached_per_domain()
    {
        using var authority = CreateAuthority();
        await authority.EnsureTrustedAsync();

        var first = authority.GetServerCertificate("chatgpt.com");
        var second = authority.GetServerCertificate("chatgpt.com");

        first.Should().NotBeNull();
        second.Should().BeSameAs(first);
    }

    [Fact]
    public async Task GetServerCertificate_returns_null_before_trust_and_after_removal()
    {
        using var authority = CreateAuthority();

        authority.GetServerCertificate("chatgpt.com").Should().BeNull();

        await authority.EnsureTrustedAsync();
        authority.GetServerCertificate("chatgpt.com").Should().NotBeNull();

        await authority.RemoveTrustAsync();

        authority.IsReady.Should().BeFalse();
        authority.GetServerCertificate("chatgpt.com").Should().BeNull();
        _trustStore.Certificates.Should().BeEmpty();
    }

    [Fact]
    public async Task RemoveTrust_is_idempotent()
    {
        using var authority = CreateAuthority();
        await authority.EnsureTrustedAsync();

        var first = await authority.RemoveTrustAsync();
        var second = await authority.RemoveTrustAsync();

        first.Ready.Should().BeTrue();
        second.Ready.Should().BeTrue();
        _trustStore.Certificates.Should().BeEmpty();
    }

    [Theory]
    [InlineData("not a domain")]
    [InlineData("localhost")]
    [InlineData("")]
    public async Task Invalid_domains_are_rejected_without_throwing(string domain)
    {
        using var authority = CreateAuthority();
        await authority.EnsureTrustedAsync();

        authority.GetServerCertificate(domain).Should().BeNull();
    }

    [Fact]
    public async Task Corrupt_root_file_is_replaced_by_a_new_root()
    {
        Directory.CreateDirectory(_directory);
        File.WriteAllText(
            Path.Combine(_directory, CertificateAuthorityOptions.RootFileName),
            "EDUCA1-not-a-real-dpapi-blob");

        using var authority = CreateAuthority();
        var result = await authority.EnsureTrustedAsync();

        result.Ready.Should().BeTrue();
        authority.GetServerCertificate("chatgpt.com").Should().NotBeNull();
        _trustStore.Certificates.Should().ContainSingle();
    }

    [Fact]
    public async Task Stale_same_subject_root_is_removed_before_trusting_the_current_one()
    {
        using var staleKey = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var request = new CertificateRequest(
            "CN=EduFocus Local Blocking CA, O=EduFocus",
            staleKey,
            HashAlgorithmName.SHA256);
        using var stale = request.CreateSelfSigned(
            DateTimeOffset.UtcNow.AddDays(-2),
            DateTimeOffset.UtcNow.AddDays(30));
        _trustStore.Add(stale);

        using var authority = CreateAuthority();
        var result = await authority.EnsureTrustedAsync();

        result.Ready.Should().BeTrue();
        _trustStore.Certificates.Should().ContainSingle();
        _trustStore.Certificates.Keys.Should().NotContain(stale.Thumbprint);
    }

    [Fact]
    public async Task Logs_never_contain_blocked_domains_or_key_material()
    {
        using var authority = CreateAuthority();
        await authority.EnsureTrustedAsync();
        authority.GetServerCertificate("chatgpt.com");
        await authority.RemoveTrustAsync();

        var messages = string.Join(
            "\n",
            _logger.Infos.Concat(_logger.Warnings).Concat(_logger.Errors));

        messages.Should().NotContain("chatgpt.com");
        messages.Should().NotContain("PRIVATE KEY");
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_directory))
                Directory.Delete(_directory, recursive: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
        }
    }

    private LocalCertificateAuthority CreateAuthority() =>
        new(
            new CertificateAuthorityOptions
            {
                Directory = _directory,
                RestrictDirectoryAcl = false
            },
            _trustStore,
            _logger);

    private sealed class InMemoryTrustStore : ICertificateTrustStore
    {
        public Dictionary<string, X509Certificate2> Certificates { get; } =
            new(StringComparer.OrdinalIgnoreCase);

        public IReadOnlyList<string> FindThumbprintsBySubject(string subjectDistinguishedName)
        {
            var subject = new X500DistinguishedName(subjectDistinguishedName);

            return Certificates.Values
                .Where(certificate => certificate.SubjectName.RawData.AsSpan().SequenceEqual(subject.RawData))
                .Select(certificate => certificate.Thumbprint)
                .ToArray();
        }

        public bool Contains(string thumbprint) => Certificates.ContainsKey(thumbprint);

        public void Add(X509Certificate2 certificate) =>
            Certificates[certificate.Thumbprint] = X509CertificateLoader.LoadCertificate(certificate.RawData);

        public void Remove(string thumbprint)
        {
            if (Certificates.Remove(thumbprint, out var certificate))
                certificate.Dispose();
        }
    }
}