using System.Net;
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
/// host allowlist is enforced again here as defence in depth.
/// </summary>
public sealed class WebPushSender : IPushSender
{
    private const int TimeToLiveSeconds = 3600;

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
            _client = new PushServiceClient
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
            logger.LogError(
                "Failed to initialize Web Push sender: {ErrorType}",
                exception.GetType().Name);
            throw;
        }
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
