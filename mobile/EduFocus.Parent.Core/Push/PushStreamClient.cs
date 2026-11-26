using System.Diagnostics;
using System.Net;
using System.Net.WebSockets;
using EduFocus.Parent.Core.Pairing;
using EduFocus.Parent.Core.Presentation;
using EduFocus.Parent.Core.Services;

namespace EduFocus.Parent.Core.Push;

public interface IPushStreamClient
{
    bool IsConnected { get; }

    Task RunAsync(Action<byte[]> onMessage, CancellationToken cancellationToken);
}

public sealed class PushStreamClient(
    ITokenStore tokenStore,
    AppState appState,
    LinkStateRestorer restorer,
    SessionExpiryService? sessionExpiry = null) : IPushStreamClient
{
    public const int MaxMessageBytes = 16 * 1024;
    private const string StreamPath = "/v1/push/stream";
    private static readonly TimeSpan InitialRetryDelay = TimeSpan.FromSeconds(2);
    private static readonly TimeSpan MaxRetryDelay = TimeSpan.FromSeconds(60);
    private static readonly TimeSpan MissingStateDelay = TimeSpan.FromSeconds(10);
    private static readonly TimeSpan ConnectTimeout = TimeSpan.FromSeconds(20);
    private static readonly TimeSpan StableConnectionDuration = TimeSpan.FromSeconds(30);

    private volatile bool _isConnected;

    public bool IsConnected => _isConnected;

    public async Task RunAsync(Action<byte[]> onMessage, CancellationToken cancellationToken)
    {
        var retryDelay = InitialRetryDelay;
        while (!cancellationToken.IsCancellationRequested)
        {
            using var socket = new ClientWebSocket();
            try
            {
                if (!appState.IsLinked
                    || string.IsNullOrWhiteSpace(appState.CloudBaseUrl))
                {
                    if (!await restorer.RestoreAsync(cancellationToken))
                        return;
                }

                if (!appState.IsLinked
                    || string.IsNullOrWhiteSpace(appState.CloudBaseUrl))
                {
                    await Task.Delay(MissingStateDelay, cancellationToken);
                    continue;
                }

                var token = await tokenStore.GetTokenAsync(cancellationToken);
                if (string.IsNullOrWhiteSpace(token))
                {
                    if (!await restorer.RestoreAsync(cancellationToken))
                        return;

                    token = await tokenStore.GetTokenAsync(cancellationToken);
                    if (string.IsNullOrWhiteSpace(token))
                    {
                        await Task.Delay(MissingStateDelay, cancellationToken);
                        continue;
                    }
                }

                socket.Options.SetRequestHeader("Authorization", $"Bearer {token}");
                socket.Options.KeepAliveInterval = TimeSpan.FromSeconds(20);
                socket.Options.CollectHttpResponseDetails = true;

                using var connectCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                connectCts.CancelAfter(ConnectTimeout);
                await socket.ConnectAsync(
                    BuildStreamUri(appState.CloudBaseUrl),
                    connectCts.Token);

                var connectedAt = DateTimeOffset.UtcNow;
                _isConnected = true;
                var closedCleanly = await ReceiveLoopAsync(socket, onMessage, cancellationToken);
                if (closedCleanly && DateTimeOffset.UtcNow - connectedAt >= StableConnectionDuration)
                {
                    retryDelay = InitialRetryDelay;
                }
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                break;
            }
            catch (WebSocketException exception) when (socket.HttpStatusCode == HttpStatusCode.Unauthorized)
            {
                Debug.WriteLine($"Push stream rejected with 401: {exception.GetType().Name}");
                if (sessionExpiry is not null)
                    await sessionExpiry.HandleUnauthorizedAsync(cancellationToken);
                return;
            }
            catch (Exception exception)
            {
                Debug.WriteLine($"Push stream connection failed: {exception.GetType().Name}");
            }
            finally
            {
                _isConnected = false;
            }

            try
            {
                await Task.Delay(retryDelay, cancellationToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }

            retryDelay = TimeSpan.FromSeconds(
                Math.Min(retryDelay.TotalSeconds * 2, MaxRetryDelay.TotalSeconds));
        }
    }

    public static Uri BuildStreamUri(string cloudBaseUrl)
    {
        if (!Uri.TryCreate(cloudBaseUrl.Trim(), UriKind.Absolute, out var baseUri))
        {
            throw new UriFormatException("Cloud base URL is invalid.");
        }

        var scheme = baseUri.Scheme.Equals(Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase)
            ? "wss"
            : "ws";
        var builder = new UriBuilder(baseUri)
        {
            Scheme = scheme,
            Port = baseUri.IsDefaultPort ? -1 : baseUri.Port,
            Path = $"{baseUri.AbsolutePath.TrimEnd('/')}{StreamPath}",
            Query = string.Empty,
            Fragment = string.Empty,
        };

        return builder.Uri;
    }

    public static async Task<bool> ReceiveLoopAsync(
        WebSocket socket,
        Action<byte[]> onMessage,
        CancellationToken cancellationToken)
    {
        var buffer = new byte[8192];
        var message = new MemoryStream();
        try
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                var result = await socket.ReceiveAsync(
                    buffer.AsMemory(),
                    cancellationToken);
                if (result.MessageType == WebSocketMessageType.Close)
                {
                    await CloseOutputAsync(socket);
                    return true;
                }

                if (message.Length + result.Count > MaxMessageBytes)
                {
                    await ClosePolicyViolationAsync(socket);
                    return false;
                }

                message.Write(buffer, 0, result.Count);
                if (!result.EndOfMessage)
                {
                    continue;
                }

                var payload = message.ToArray();
                message.SetLength(0);
                if (message.Capacity > MaxMessageBytes)
                {
                    message.Dispose();
                    message = new MemoryStream();
                }

                if (result.MessageType != WebSocketMessageType.Text || payload.Length == 0)
                {
                    continue;
                }

                try
                {
                    onMessage(payload);
                }
                catch (Exception exception)
                {
                    Debug.WriteLine($"Push stream message failed: {exception.GetType().Name}");
                }
            }

            return true;
        }
        finally
        {
            message.Dispose();
        }
    }

    private static async Task CloseOutputAsync(WebSocket socket)
    {
        try
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(2));
            await socket.CloseOutputAsync(
                WebSocketCloseStatus.NormalClosure,
                "closed",
                timeout.Token);
        }
        catch (Exception exception) when (
            exception is WebSocketException or ObjectDisposedException or OperationCanceledException)
        {
        }
    }

    private static async Task ClosePolicyViolationAsync(WebSocket socket)
    {
        try
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(2));
            await socket.CloseAsync(
                WebSocketCloseStatus.MessageTooBig,
                "message_too_big",
                timeout.Token);
        }
        catch (Exception exception) when (
            exception is WebSocketException or ObjectDisposedException or OperationCanceledException)
        {
            socket.Abort();
        }
    }
}
