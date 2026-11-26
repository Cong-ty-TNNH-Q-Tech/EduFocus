using EduFocus.Parent.Core.Push;

namespace EduFocus.Parent.Tests;

public sealed class PushStreamClientTests
{
    [Theory]
    [InlineData("https://cloud.example.com", "wss://cloud.example.com/v1/push/stream")]
    [InlineData("https://cloud.example.com/", "wss://cloud.example.com/v1/push/stream")]
    [InlineData("http://10.0.2.2:8080", "ws://10.0.2.2:8080/v1/push/stream")]
    [InlineData("https://cloud.example.com:8443", "wss://cloud.example.com:8443/v1/push/stream")]
    [InlineData("https://cloud.example.com/base/", "wss://cloud.example.com/base/v1/push/stream")]
    public void Stream_uri_uses_websocket_scheme_and_api_path(
        string baseUrl,
        string expected)
    {
        Assert.Equal(expected, PushStreamClient.BuildStreamUri(baseUrl).ToString());
    }

    [Fact]
    public void Invalid_base_url_is_rejected()
    {
        Assert.Throws<UriFormatException>(
            () => PushStreamClient.BuildStreamUri("not a url"));
    }
}
