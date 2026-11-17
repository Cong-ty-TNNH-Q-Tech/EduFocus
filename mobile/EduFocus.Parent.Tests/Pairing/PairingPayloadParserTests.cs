using System.Text;
using EduFocus.Cloud.Contracts.Pairing;
using EduFocus.Parent.Core.Pairing;

namespace EduFocus.Parent.Tests.Pairing;

public sealed class PairingPayloadParserTests
{
    private const string SessionId = "6f7d1a6e-2f4f-4e3b-9b8e-7c1f5c0a1b2c";

    [Fact]
    public void Parses_https_payload_built_by_desktop()
    {
        var raw = PairingUri.Build("https://cloud.example.com", Guid.Parse(SessionId), "ABCD2345");

        var parsed = PairingPayloadParser.TryParse(raw, allowInsecureEndpoints: false, out var payload, out var error);

        Assert.True(parsed, error);
        Assert.NotNull(payload);
        Assert.Equal("https://cloud.example.com", payload!.CloudBaseUrl);
        Assert.Equal(Guid.Parse(SessionId), payload.SessionId);
        Assert.Equal("ABCD2345", payload.Code);
    }

    [Theory]
    [InlineData("https://example.com/pair?v=1")]
    [InlineData("edufocusx://pair?v=1&u=a&s=b&c=c")]
    [InlineData("")]
    [InlineData(null)]
    public void Rejects_foreign_payloads(string? raw)
    {
        Assert.False(PairingPayloadParser.TryParse(raw, false, out var payload, out var error));
        Assert.Null(payload);
        Assert.False(string.IsNullOrWhiteSpace(error));
    }

    [Fact]
    public void Rejects_unknown_version()
    {
        var raw = $"edufocus://pair?v=2&u={Encode("https://cloud.example.com")}&s={SessionId}&c=ABCD2345";

        Assert.False(PairingPayloadParser.TryParse(raw, false, out _, out var error));
        Assert.Contains("phiên bản", error);
    }

    [Fact]
    public void Rejects_http_payload_outside_development()
    {
        var raw = PairingUri.Build("http://192.168.1.20:5281", Guid.Parse(SessionId), "ABCD2345");

        Assert.False(PairingPayloadParser.TryParse(raw, allowInsecureEndpoints: false, out _, out var error));
        Assert.Contains("HTTPS", error);
    }

    [Fact]
    public void Allows_http_lan_payload_in_development()
    {
        var raw = PairingUri.Build("http://192.168.1.20:5281", Guid.Parse(SessionId), "ABCD2345");

        var parsed = PairingPayloadParser.TryParse(raw, allowInsecureEndpoints: true, out var payload, out var error);

        Assert.True(parsed, error);
        Assert.Equal("http://192.168.1.20:5281", payload!.CloudBaseUrl);
    }

    [Fact]
    public void Insecure_development_mode_still_rejects_public_http_hosts()
    {
        var raw = PairingUri.Build("http://cloud.example.com", Guid.Parse(SessionId), "ABCD2345");

        Assert.False(PairingPayloadParser.TryParse(raw, allowInsecureEndpoints: true, out _, out var error));
        Assert.Contains("LAN", error);
    }

    [Fact]
    public void Rejects_malformed_base64_url()
    {
        var raw = $"edufocus://pair?v=1&u=***&s={SessionId}&c=ABCD2345";

        Assert.False(PairingPayloadParser.TryParse(raw, false, out _, out var error));
        Assert.False(string.IsNullOrWhiteSpace(error));
    }

    [Fact]
    public void Rejects_non_guid_session()
    {
        var raw = $"edufocus://pair?v=1&u={Encode("https://cloud.example.com")}&s=not-a-guid&c=ABCD2345";

        Assert.False(PairingPayloadParser.TryParse(raw, false, out _, out _));
    }

    [Theory]
    [InlineData("ABCD234")]
    [InlineData("ABCD23456")]
    [InlineData("ABCD234U")]
    public void Rejects_invalid_codes(string code)
    {
        var raw = $"edufocus://pair?v=1&u={Encode("https://cloud.example.com")}&s={SessionId}&c={code}";

        Assert.False(PairingPayloadParser.TryParse(raw, false, out _, out var error));
        Assert.Contains("Mã ghép nối", error);
    }

    [Fact]
    public void Normalizes_lowercase_and_dashes_in_code()
    {
        var raw = $"edufocus://pair?v=1&u={Encode("https://cloud.example.com")}&s={SessionId}&c=abcd-2345";

        var parsed = PairingPayloadParser.TryParse(raw, false, out var payload, out var error);

        Assert.True(parsed, error);
        Assert.Equal("ABCD2345", payload!.Code);
    }

    private static string Encode(string value) =>
        Convert.ToBase64String(Encoding.UTF8.GetBytes(value))
            .TrimEnd('=')
            .Replace('+', '-')
            .Replace('/', '_');
}
