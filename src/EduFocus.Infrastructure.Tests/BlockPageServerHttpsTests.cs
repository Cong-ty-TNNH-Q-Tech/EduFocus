using System.Net;
using System.Net.Security;
using System.Net.Sockets;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using EduFocus.Infrastructure.Protection;
using EduFocus.Infrastructure.Protection.Certificates;
using FluentAssertions;

namespace EduFocus.Infrastructure.Tests;

public sealed class BlockPageServerHttpsTests : IDisposable
{
    private readonly string _directory = Path.Combine(
        Path.GetTempPath(),
        "edufocus-ca-tests",
        Guid.NewGuid().ToString("N"));

    private readonly InMemoryTrustStore _trustStore = new();
    private readonly TestLogger _logger = new();
    private LocalCertificateAuthority? _authority;

    [Fact]
    public async Task Https_listener_serves_the_block_page_with_a_domain_certificate()
    {
        var server = await CreateServerAsync(trustAuthority: true);

        await using (server)
        {
            var start = await server.StartAsync(["chatgpt.com"]);

            start.Started.Should().BeTrue();
            start.Warning.Should().BeNull();
            server.HttpsPort.Should().BeGreaterThan(0);

            var result = await SendOverTlsAsync(server.HttpsPort, "chatgpt.com", "chatgpt.com");

            result.Response.StatusCode.Should().Be(200);
            result.Response.Body.Should().Contain("Trang này không nằm trong môi trường học tập.");
            result.Response.Body.Should().Contain("chatgpt.com");
            result.Response.Headers["content-security-policy"].Should().Contain("default-src 'none'");

            result.Certificate.Should().NotBeNull();
            var san = result.Certificate!.Extensions
                .OfType<X509SubjectAlternativeNameExtension>()
                .Single();
            san.EnumerateDnsNames().Should().Contain("chatgpt.com");
            result.Certificate.Issuer.Should().Contain("EduFocus Local Blocking CA");
        }
    }

    [Fact]
    public async Task Https_listener_is_skipped_when_the_authority_is_not_trusted()
    {
        var server = await CreateServerAsync(trustAuthority: false);

        await using (server)
        {
            var start = await server.StartAsync(["chatgpt.com"]);

            start.Started.Should().BeTrue();
            server.Port.Should().BeGreaterThan(0);
            server.HttpsPort.Should().Be(0);
        }
    }

    [Fact]
    public async Task Unknown_sni_fails_the_handshake()
    {
        var server = await CreateServerAsync(trustAuthority: true);

        await using (server)
        {
            await server.StartAsync(["chatgpt.com"]);

            var act = async () =>
                await SendOverTlsAsync(server.HttpsPort, "example.org", "example.org");

            await act.Should().ThrowAsync<Exception>();
        }
    }

    [Fact]
    public async Task Host_header_is_validated_even_over_tls()
    {
        var server = await CreateServerAsync(trustAuthority: true);

        await using (server)
        {
            await server.StartAsync(["chatgpt.com"]);

            var result = await SendOverTlsAsync(server.HttpsPort, "chatgpt.com", "example.org");

            result.Response.StatusCode.Should().Be(404);
            result.Response.Body.Should().NotContain("example.org");
            result.Response.Body.Should().Contain("Không tìm thấy trang.");
        }
    }

    [Fact]
    public async Task Https_hit_records_the_domain_without_path_or_query()
    {
        var server = await CreateServerAsync(trustAuthority: true);

        await using (server)
        {
            var hits = new List<string>();
            server.BlockedRequestObserved += hit => hits.Add(hit.Domain);
            await server.StartAsync(["chatgpt.com"]);

            await SendOverTlsAsync(
                server.HttpsPort,
                "chatgpt.com",
                "chatgpt.com",
                "/chat?token=secret");

            hits.Should().ContainSingle().Which.Should().Be("chatgpt.com");
        }
    }

    public void Dispose()
    {
        _authority?.Dispose();

        try
        {
            if (Directory.Exists(_directory))
                Directory.Delete(_directory, recursive: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
        }
    }

    private async Task<BlockPageServer> CreateServerAsync(bool trustAuthority)
    {
        _authority = new LocalCertificateAuthority(
            new CertificateAuthorityOptions
            {
                Directory = _directory,
                RestrictDirectoryAcl = false
            },
            _trustStore,
            _logger);

        if (trustAuthority)
        {
            var result = await _authority.EnsureTrustedAsync();
            result.Ready.Should().BeTrue();
        }

        return new BlockPageServer(
            new BlockPageOptions { Port = 0, HttpsPort = 0 },
            _logger,
            _authority);
    }

    private static async Task<TlsResult> SendOverTlsAsync(
        int port,
        string serverName,
        string hostHeader,
        string target = "/")
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        using var client = new TcpClient();
        await client.ConnectAsync(IPAddress.Loopback, port, timeout.Token);

        X509Certificate2? remoteCertificate = null;
        await using var ssl = new SslStream(
            client.GetStream(),
            leaveInnerStreamOpen: false,
            (_, certificate, _, _) =>
            {
                if (certificate is not null)
                    remoteCertificate = X509CertificateLoader.LoadCertificate(certificate.GetRawCertData());

                return true;
            });

        await ssl.AuthenticateAsClientAsync(
            new SslClientAuthenticationOptions
            {
                TargetHost = serverName,
                CertificateRevocationCheckMode = X509RevocationMode.NoCheck,
                ApplicationProtocols = [SslApplicationProtocol.Http11]
            },
            timeout.Token);

        var request = $"GET {target} HTTP/1.1\r\nHost: {hostHeader}\r\nConnection: close\r\n\r\n";
        await ssl.WriteAsync(Encoding.ASCII.GetBytes(request), timeout.Token);
        await ssl.FlushAsync(timeout.Token);

        using var buffer = new MemoryStream();
        var chunk = new byte[4096];
        while (true)
        {
            var read = await ssl.ReadAsync(chunk, timeout.Token);
            if (read == 0)
                break;

            buffer.Write(chunk, 0, read);
        }

        return new TlsResult(TlsResponse.Parse(buffer.ToArray()), remoteCertificate);
    }

    private sealed record TlsResult(TlsResponse Response, X509Certificate2? Certificate);

    private sealed record TlsResponse(
        int StatusCode,
        Dictionary<string, string> Headers,
        string Body)
    {
        public static TlsResponse Parse(byte[] raw)
        {
            var separator = raw.AsSpan().IndexOf("\r\n\r\n"u8);
            if (separator < 0)
                return new TlsResponse(0, [], string.Empty);

            var headerText = Encoding.ASCII.GetString(raw.AsSpan(0, separator));
            var lines = headerText.Split("\r\n");
            var statusParts = lines[0].Split(' ', StringSplitOptions.RemoveEmptyEntries);
            var status = statusParts.Length > 1 && int.TryParse(statusParts[1], out var parsed)
                ? parsed
                : 0;

            var headers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var line in lines.Skip(1))
            {
                var colon = line.IndexOf(':');
                if (colon > 0)
                    headers[line[..colon].Trim()] = line[(colon + 1)..].Trim();
            }

            var body = Encoding.UTF8.GetString(raw.AsSpan(separator + 4));
            return new TlsResponse(status, headers, body);
        }
    }

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