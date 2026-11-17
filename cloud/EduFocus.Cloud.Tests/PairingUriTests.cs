using EduFocus.Cloud.Contracts.Pairing;

namespace EduFocus.Cloud.Tests;

public sealed class PairingUriTests
{
    [Fact]
    public void Pairing_uri_round_trips()
    {
        var sessionId = Guid.Parse("6f1c2d3e-4a5b-4c6d-8e7f-901234567890");

        var uri = PairingUri.Build("https://cloud.example.com/", sessionId, "ABCD2345");

        Assert.Equal(
            "edufocus://pair?v=1&u=aHR0cHM6Ly9jbG91ZC5leGFtcGxlLmNvbQ&s=6f1c2d3e-4a5b-4c6d-8e7f-901234567890&c=ABCD2345",
            uri);
        Assert.True(PairingUri.TryParse(uri, out var data));
        Assert.Equal("https://cloud.example.com", data.CloudBaseUrl);
        Assert.Equal(sessionId, data.SessionId);
        Assert.Equal("ABCD2345", data.Code);
    }

    [Fact]
    public void Pairing_uri_normalizes_code_case_on_parse()
    {
        var uri = PairingUri.Build("http://localhost:8080", Guid.NewGuid(), "abcd2345");

        Assert.True(PairingUri.TryParse(uri, out var data));
        Assert.Equal("ABCD2345", data.Code);
    }

    [Theory]
    [InlineData("")]
    [InlineData("https://cloud.example.com/pair?v=1&u=aHR0cHM6Ly9jbG91ZC5leGFtcGxl&s=6f1c2d3e-4a5b-4c6d-8e7f-901234567890&c=ABCD2345")]
    [InlineData("edufocus://pair?v=2&u=aHR0cHM6Ly9jbG91ZC5leGFtcGxl&s=6f1c2d3e-4a5b-4c6d-8e7f-901234567890&c=ABCD2345")]
    [InlineData("edufocus://pair?v=1&u=!!!&s=6f1c2d3e-4a5b-4c6d-8e7f-901234567890&c=ABCD2345")]
    [InlineData("edufocus://pair?v=1&u=aHR0cHM6Ly9jbG91ZC5leGFtcGxl&s=not-a-guid&c=ABCD2345")]
    [InlineData("edufocus://pair?v=1&u=aHR0cHM6Ly9jbG91ZC5leGFtcGxl&s=6f1c2d3e-4a5b-4c6d-8e7f-901234567890")]
    [InlineData("edufocus://pair?v=1&u=bm90LWEtdXJs&s=6f1c2d3e-4a5b-4c6d-8e7f-901234567890&c=ABCD2345")]
    public void Invalid_pairing_uri_is_rejected(string value)
    {
        Assert.False(PairingUri.TryParse(value, out _));
    }
}
