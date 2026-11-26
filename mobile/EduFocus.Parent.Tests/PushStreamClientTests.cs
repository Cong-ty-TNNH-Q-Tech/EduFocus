using System.Net.WebSockets;
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

    [Fact]
    public async Task Receive_loop_rejects_oversized_messages()
    {
        using var socket = new FakeWebSocket(new byte[PushStreamClient.MaxMessageBytes + 1]);
        var delivered = 0;

        var ok = await PushStreamClient.ReceiveLoopAsync(socket, _ => delivered++, CancellationToken.None);

        Assert.False(ok);
        Assert.Equal(0, delivered);
        Assert.Equal(WebSocketCloseStatus.MessageTooBig, socket.ClosedStatus);
    }

    private sealed class FakeWebSocket(byte[] payload) : WebSocket
    {
        private int _offset;

        public WebSocketCloseStatus? ClosedStatus { get; private set; }

        public override WebSocketCloseStatus? CloseStatus => ClosedStatus;
        public override string? CloseStatusDescription => null;
        public override WebSocketState State => ClosedStatus is null ? WebSocketState.Open : WebSocketState.Closed;
        public override string? SubProtocol => null;

        public override void Abort() => ClosedStatus = WebSocketCloseStatus.EndpointUnavailable;

        public override Task CloseAsync(WebSocketCloseStatus closeStatus, string? statusDescription, CancellationToken cancellationToken)
        {
            ClosedStatus = closeStatus;
            return Task.CompletedTask;
        }

        public override Task CloseOutputAsync(WebSocketCloseStatus closeStatus, string? statusDescription, CancellationToken cancellationToken)
        {
            ClosedStatus = closeStatus;
            return Task.CompletedTask;
        }

        public override void Dispose()
        {
        }

        public override Task<WebSocketReceiveResult> ReceiveAsync(ArraySegment<byte> buffer, CancellationToken cancellationToken)
        {
            if (_offset >= payload.Length)
            {
                return Task.FromResult(new WebSocketReceiveResult(0, WebSocketMessageType.Close, true, WebSocketCloseStatus.NormalClosure, "done"));
            }

            var chunk = Math.Min(buffer.Count, payload.Length - _offset);
            Array.Copy(payload, _offset, buffer.Array!, buffer.Offset, chunk);
            _offset += chunk;
            return Task.FromResult(new WebSocketReceiveResult(chunk, WebSocketMessageType.Text, endOfMessage: _offset >= payload.Length));
        }

        public override Task SendAsync(ArraySegment<byte> buffer, WebSocketMessageType messageType, bool endOfMessage, CancellationToken cancellationToken) =>
            Task.CompletedTask;
    }
}
