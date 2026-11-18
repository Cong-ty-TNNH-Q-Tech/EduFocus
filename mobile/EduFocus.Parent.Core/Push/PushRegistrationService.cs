using System.Diagnostics;
using EduFocus.Cloud.Contracts.Push;
using EduFocus.Parent.Core.Presentation;
using EduFocus.Parent.Core.Services;
using EduFocus.Parent.Core.Settings;

namespace EduFocus.Parent.Core.Push;

public interface IPushTokenProvider
{
    Task<string?> GetTokenAsync(CancellationToken cancellationToken = default);
}

public interface IPushRegistrationService
{
    Task<bool> RegisterAsync(
        string? fcmToken,
        string platform,
        string? appVersion,
        bool force = false,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// Registers the FCM token with the cloud using the mobile token. Network and
/// server errors retry with backoff; auth/invalid-token errors stop so the app
/// does not hammer the API. The token itself is only ever sent in the request
/// body and stored in preferences, never in messages or logs.
/// </summary>
public sealed class PushRegistrationService(
    ICloudApiClient api,
    AppState appState,
    ParentSettingsStore settings,
    SessionExpiryService? sessionExpiry = null,
    Func<TimeSpan, CancellationToken, Task>? delay = null) : IPushRegistrationService
{
    private const string RegistrationsPath = "/v1/push/registrations";
    private const int MaxAttempts = 3;
    private const int MaxTokenLength = 512;
    private const int MaxPlatformLength = 32;
    private const int MaxAppVersionLength = 32;
    private static readonly TimeSpan[] Backoff =
    [
        TimeSpan.FromSeconds(2),
        TimeSpan.FromSeconds(5),
    ];

    public async Task<bool> RegisterAsync(
        string? fcmToken,
        string platform,
        string? appVersion,
        bool force = false,
        CancellationToken cancellationToken = default)
    {
        if (!appState.IsLinked)
            return false;

        var token = fcmToken?.Trim();
        if (string.IsNullOrEmpty(token)
            || token.Length > MaxTokenLength
            || token.Any(char.IsControl))
        {
            return false;
        }

        var platformName = Truncate(platform, MaxPlatformLength);
        if (platformName is null)
            return false;

        var version = Truncate(appVersion, MaxAppVersionLength);
        if (!force
            && string.Equals(settings.PushToken, token, StringComparison.Ordinal)
            && string.Equals(settings.PushAppVersion, version, StringComparison.Ordinal))
        {
            return true;
        }

        var wait = delay ?? Task.Delay;
        for (var attempt = 1; attempt <= MaxAttempts; attempt++)
        {
            try
            {
                await api.PostAsync<RegisterPushRequest, RegisterPushResponse>(
                    RegistrationsPath,
                    new RegisterPushRequest(token, platformName, version),
                    cancellationToken: cancellationToken);

                settings.PushToken = token;
                settings.PushAppVersion = version;
                return true;
            }
            catch (CloudApiException ex)
            {
                if (ex.StatusCode == 401)
                {
                    if (sessionExpiry is not null)
                        await sessionExpiry.HandleUnauthorizedAsync(cancellationToken);

                    return false;
                }

                if (ex.StatusCode is 400 or 403 or 404)
                {
                    Trace.TraceWarning(
                        "Push registration failed with status {0} ({1}).",
                        ex.StatusCode,
                        ex.Code);
                    return false;
                }

                if (attempt == MaxAttempts)
                    return false;

                await wait(RetryDelayOf(ex, attempt), cancellationToken);
            }
            catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
            {
                if (attempt == MaxAttempts)
                    return false;

                await wait(Backoff[attempt - 1], cancellationToken);
            }
        }

        return false;
    }

    private static TimeSpan RetryDelayOf(CloudApiException exception, int attempt)
    {
        if (exception.StatusCode == 429
            && exception.RetryAfter is { } retryAfter
            && retryAfter > TimeSpan.Zero)
        {
            return retryAfter > TimeSpan.FromMinutes(1) ? TimeSpan.FromMinutes(1) : retryAfter;
        }

        return Backoff[attempt - 1];
    }

    private static string? Truncate(string? value, int maxLength)
    {
        var trimmed = value?.Trim();
        if (string.IsNullOrEmpty(trimmed))
            return null;

        return trimmed.Length <= maxLength ? trimmed : trimmed[..maxLength];
    }
}
