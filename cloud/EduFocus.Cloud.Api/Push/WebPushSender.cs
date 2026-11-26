using System.Net;
using System.Net.Sockets;
using System.Text.Json;
using EduFocus.Cloud.Api.Options;
using Lib.Net.Http.WebPush;
using Lib.Net.Http.WebPush.Authentication;
using Microsoft.Extensions.Options;
using WebPushMessage = Lib.Net.Http.WebPush.PushMessage;

namespace EduFocus.Cloud.Api.Push;

public sealed record PushTarget(string Endpoint, string P256dh, string Auth);

public sealed record PushSendResult(
    string Endpoint,
    bool Success,
    bool EndpointInvalid,
    string? ErrorCode);

public interface IPushSender
{
    Task<IReadOnlyList<PushSendResult>> SendAsync(
        IReadOnlyList<PushTarget> targets,
        IReadOnlyDictionary<string, string> data,
        CancellationToken cancellationToken);
}

/// <summary>
/// Sends encrypted Web Push requests through the UnifiedPush endpoint supplied
/// by the phone's distributor. VAPID authentication is required; the endpoint
/// host allowlist and public-IP dial validation are enforced as defence in depth.
/// </summary>
public sealed class WebPushSender : IPushSender, IDisposable
{
    private const int TimeToLiveSeconds = 3600;

    private readonly HttpClient? _httpClient;
    private readonly PushServiceClient? _client;
    private readonly IReadOnlyList<string> _allowedHosts;
    private readonly ILogger<WebPushSender> _logger;

    public WebPushSender(IOptions<PushOptions> options, ILogger<WebPushSender> logger)
    {
        var value = options.Value;
        _allowedHosts = value.AllowedEndpointHosts;
        _logger = logger;

        if (!value.Enabled)
            return;

        try
        {
            var handler = new SocketsHttpHandler
            {
                AllowAutoRedirect = false,
                ConnectTimeout = TimeSpan.FromSeconds(5),
                ConnectCallback = ConnectPublicEndpointAsync,
            };
            _httpClient = new HttpClient(handler)
            {
                Timeout = TimeSpan.FromSeconds(10),
            };
            _client = new PushServiceClient(_httpClient)
            {
                AutoRetryAfter = false,
                DefaultTimeToLive = TimeToLiveSeconds,
            };
            _client.DefaultAuthentication = new VapidAuthentication(
                value.VapidPublicKey!,
                value.VapidPrivateKey!)
            {
                Subject = value.VapidSubject!,
            };
        }
        catch (Exception exception)
        {
            _httpClient?.Dispose();
            _httpClient = null;
            logger.LogError(
                "Failed to initialize Web Push sender: {ErrorType}",
                exception.GetType().Name);
            throw;
        }
    }

    public void Dispose() => _httpClient?.Dispose();

    private static async ValueTask<Stream> ConnectPublicEndpointAsync(
        SocketsHttpConnectionContext context,
        CancellationToken cancellationToken)
    {
        var addresses = await Dns.GetHostAddressesAsync(context.DnsEndPoint.Host, cancellationToken);
        var publicAddresses = addresses.Where(address => !IsDisallowedIpAddress(address)).ToArray();
        if (publicAddresses.Length == 0)
        {
            throw new HttpRequestException("Push destination resolved only to disallowed addresses.");
        }

        var socket = new Socket(SocketType.Stream, ProtocolType.Tcp)
        {
            NoDelay = true,
        };
        try
        {
            await socket.ConnectAsync(publicAddresses, context.DnsEndPoint.Port, cancellationToken);
            return new NetworkStream(socket, ownsSocket: true);
        }
        catch
        {
            socket.Dispose();
            throw;
        }
    }

    public static bool IsDisallowedIpAddress(IPAddress address)
    {
        if (address.IsIPv4MappedToIPv6)
        {
            address = address.MapToIPv4();
        }

        if (IPAddress.IsLoopback(address))
        {
            return true;
        }

        if (address.AddressFamily == AddressFamily.InterNetwork)
        {
            var b = address.GetAddressBytes();
            return b[0] == 0
                || b[0] == 10
                || b[0] == 127
                || (b[0] == 100 && b[1] >= 64 && b[1] <= 127)
                || (b[0] == 169 && b[1] == 254)
                || (b[0] == 172 && b[1] >= 16 && b[1] <= 31)
                || (b[0] == 192 && b[1] == 168)
                || b[0] >= 224;
        }

        if (address.AddressFamily == AddressFamily.InterNetworkV6)
        {
            if (address.IsIPv6LinkLocal
                || address.IsIPv6Multicast
                || address.IsIPv6SiteLocal
                || address.Equals(IPAddress.IPv6Any)
                || address.Equals(IPAddress.IPv6None))
            {
                return true;
            }

            var b = address.GetAddressBytes();
            return (b[0] & 0xFE) == 0xFC;
        }

        return true;
    }

    public async Task<IReadOnlyList<PushSendResult>> SendAsync(
        IReadOnlyList<PushTarget> targets,
        IReadOnlyDictionary<string, string> data,
        CancellationToken cancellationToken)
    {
        if (_client is null)
            throw new InvalidOperationException("Push is disabled.");

        var content = JsonSerializer.Serialize(data);
        var results = new List<PushSendResult>(targets.Count);
        foreach (var target in targets)
        {
            if (!PushEndpointValidator.IsEndpointAllowed(target.Endpoint, _allowedHosts))
            {
                results.Add(new PushSendResult(
                    target.Endpoint,
                    false,
                    false,
                    "endpoint_not_allowed"));
                continue;
            }

            var subscription = new PushSubscription { Endpoint = target.Endpoint };
            subscription.SetKey(PushEncryptionKeyName.P256DH, target.P256dh);
            subscription.SetKey(PushEncryptionKeyName.Auth, target.Auth);

            try
            {
                await _client.RequestPushMessageDeliveryAsync(
                    subscription,
                    new WebPushMessage(content)
                    {
                        TimeToLive = TimeToLiveSeconds,
                        Urgency = PushMessageUrgency.Normal,
                    },
                    cancellationToken);

                results.Add(new PushSendResult(target.Endpoint, true, false, null));
            }
            catch (PushServiceClientException exception)
            {
                var invalid = exception.StatusCode is HttpStatusCode.NotFound
                    or HttpStatusCode.Gone;
                results.Add(new PushSendResult(
                    target.Endpoint,
                    false,
                    invalid,
                    $"http_{(int)exception.StatusCode}"));
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception exception)
            {
                _logger.LogWarning(
                    "Web Push delivery failed with {ErrorType}",
                    exception.GetType().Name);
                results.Add(new PushSendResult(
                    target.Endpoint,
                    false,
                    false,
                    "push_unavailable"));
            }
        }

        return results;
    }
}
