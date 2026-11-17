using FirebaseAdmin;
using FirebaseAdmin.Messaging;
using Google.Apis.Auth.OAuth2;
using EduFocus.Cloud.Api.Options;
using Microsoft.Extensions.Options;

namespace EduFocus.Cloud.Api.Push;

public sealed record PushMessage(string Token, IReadOnlyDictionary<string, string> Data);

public sealed record PushSendResult(string Token, bool Success, bool TokenInvalid, string? ErrorCode);

public interface IFcmPushSender
{
    Task<IReadOnlyList<PushSendResult>> SendAsync(
        IReadOnlyList<PushMessage> messages,
        CancellationToken cancellationToken);
}

public sealed class FirebaseFcmSender : IFcmPushSender
{
    private const int MaxMessagesPerRequest = 500;

    private readonly FirebaseMessaging? _messaging;

    public FirebaseFcmSender(IOptions<PushOptions> options, ILogger<FirebaseFcmSender> logger)
    {
        var value = options.Value;
        if (!value.Enabled)
        {
            return;
        }

        try
        {
            var app = FirebaseApp.DefaultInstance;
            if (app is null)
            {
                var credential = ResolveCredential(value);
                app = FirebaseApp.Create(new AppOptions
                {
                    Credential = credential,
                    ProjectId = value.ProjectId,
                });
            }

            _messaging = FirebaseMessaging.GetMessaging(app);
        }
        catch (Exception exception)
        {
            logger.LogError(
                "Failed to initialize Firebase push sender: {ErrorType}",
                exception.GetType().Name);
            throw;
        }
    }

    public async Task<IReadOnlyList<PushSendResult>> SendAsync(
        IReadOnlyList<PushMessage> messages,
        CancellationToken cancellationToken)
    {
        if (_messaging is null)
        {
            throw new InvalidOperationException("Push is disabled.");
        }

        var results = new List<PushSendResult>(messages.Count);
        foreach (var chunk in messages.Chunk(MaxMessagesPerRequest))
        {
            var batch = chunk
                .Select(message => new Message
                {
                    Token = message.Token,
                    Data = message.Data.ToDictionary(pair => pair.Key, pair => pair.Value),
                })
                .ToList();

            try
            {
                var response = await _messaging.SendEachAsync(batch, cancellationToken);
                for (var index = 0; index < response.Responses.Count; index++)
                {
                    var sendResponse = response.Responses[index];
                    if (sendResponse.IsSuccess)
                    {
                        results.Add(new PushSendResult(chunk[index].Token, true, false, null));
                        continue;
                    }

                    var errorCode = sendResponse.Exception is FirebaseMessagingException messagingException
                        ? messagingException.MessagingErrorCode
                        : null;
                    var invalid = errorCode is MessagingErrorCode.Unregistered
                        or MessagingErrorCode.InvalidArgument;
                    results.Add(new PushSendResult(
                        chunk[index].Token,
                        false,
                        invalid,
                        errorCode?.ToString() ?? "unknown"));
                }
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception exception)
            {
                var code = exception is FirebaseMessagingException messagingException
                    ? messagingException.MessagingErrorCode?.ToString()
                    : null;
                foreach (var message in chunk)
                {
                    results.Add(new PushSendResult(
                        message.Token,
                        false,
                        false,
                        string.IsNullOrWhiteSpace(code) ? "fcm_unavailable" : code));
                }
            }
        }

        return results;
    }

    private static GoogleCredential ResolveCredential(PushOptions options)
    {
        if (!string.IsNullOrWhiteSpace(options.ServiceAccountJson))
        {
            return CredentialFactory
                .FromJson<ServiceAccountCredential>(options.ServiceAccountJson)
                .ToGoogleCredential();
        }

        if (!string.IsNullOrWhiteSpace(options.ServiceAccountPath))
        {
            return CredentialFactory
                .FromFile<ServiceAccountCredential>(options.ServiceAccountPath)
                .ToGoogleCredential();
        }

        throw new InvalidOperationException(
            "Push:ServiceAccountPath or Push:ServiceAccountJson is required when push is enabled.");
    }
}
