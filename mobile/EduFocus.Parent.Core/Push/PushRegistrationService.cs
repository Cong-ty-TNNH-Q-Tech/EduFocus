using System.Diagnostics;
using EduFocus.Cloud.Contracts.Push;
using EduFocus.Parent.Core.Presentation;
using EduFocus.Parent.Core.Services;
using EduFocus.Parent.Core.Settings;

namespace EduFocus.Parent.Core.Push;

public sealed record PushEndpointRegistration(string Endpoint, string P256dh, string Auth);

public interface IPushRegistrationService
{
    Task<bool> RegisterAsync(
        PushEndpointRegistration? endpoint,
        string platform,
        string? appVersion,
        bool force = false,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// Registers the UnifiedPush endpoint and Web Push keys with the cloud using
/// the mobile token. Network and server errors retry with backoff;
/// auth/invalid-request errors stop so the app does not hammer the API. The
/// endpoint and keys are only ever sent in the request body and stored in
/// preferences, never in messages or logs.
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
    private const int MaxEndpointLength = 1000;
    private const int MaxKeyLength = 128;
    private const int MaxPlatformLength = 32;
    private const int MaxAppVersionLength = 32;
    private static readonly TimeSpan[] Backoff =
    [
        TimeSpan.FromSeconds(2),
        TimeSpan.FromSeconds(5),
    ];

    public async Task<bool> RegisterAsync(
        PushEndpointRegistration? endpoint,
        string platform,
        string? appVersion,
        bool force = false,
        CancellationToken cancellationToken = default)
    {
        if (!appState.IsLinked)
            return false;

        if (!IsValidEndpoint(endpoint, out var normalizedEndpoint))
            return false;

        var platformName = Truncate(platform, MaxPlatformLength);
        if (platformName is null)
            return false;

        var version = Truncate(appVersion, MaxAppVersionLength);
        if (!force
            && string.Equals(settings.PushEndpoint, normalizedEndpoint.Endpoint, StringComparison.Ordinal)
            && string.Equals(settings.PushP256dh, normalizedEndpoint.P256dh, StringComparison.Ordinal)
            && string.Equals(settings.PushAuth, normalizedEndpoint.Auth, StringComparison.Ordinal)
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
                    new RegisterPushRequest(
                        normalizedEndpoint.Endpoint,
                        normalizedEndpoint.P256dh,
                        normalizedEndpoint.Auth,
                        platformName,
                        version),
                    cancellationToken: cancellationToken);

                settings.SetPushEndpoint(normalizedEndpoint);
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

    private static bool IsValidEndpoint(
        PushEndpointRegistration? registration,
        out PushEndpointRegistration normalized)
    {
        normalized = registration ?? new PushEndpointRegistration(string.Empty, string.Empty, string.Empty);
        if (registration is null)
            return false;

        var endpoint = registration.Endpoint?.Trim();
        if (string.IsNullOrEmpty(endpoint)
            || endpoint.Length > MaxEndpointLength
            || endpoint.Any(char.IsControl)
            || !Uri.TryCreate(endpoint, UriKind.Absolute, out var uri)
            || uri.Scheme != Uri.UriSchemeHttps
            || string.IsNullOrWhiteSpace(uri.Host)
            || !string.IsNullOrEmpty(uri.UserInfo))
        {
            return false;
        }

        var p256dh = registration.P256dh?.Trim();
        var auth = registration.Auth?.Trim();
        if (!IsValidBase64Url(p256dh, 65, firstByte: 0x04)
            || !IsValidBase64Url(auth, 16, firstByte: null))
        {
            return false;
        }

        normalized = new PushEndpointRegistration(endpoint, p256dh!, auth!);
        return true;
    }

    private static bool IsValidBase64Url(string? value, int expectedLength, byte? firstByte)
    {
        if (string.IsNullOrEmpty(value)
            || value.Length > MaxKeyLength
            || value.Any(ch => !char.IsAsciiLetterOrDigit(ch) && ch is not '-' and not '_'))
        {
            return false;
        }

        var padded = value.Replace('-', '+').Replace('_', '/');
        padded = (padded.Length % 4) switch
        {
            2 => padded + "==",
            3 => padded + "=",
            _ => padded,
        };

        try
        {
            var bytes = Convert.FromBase64String(padded);
            return bytes.Length == expectedLength
                && (firstByte is null || bytes[0] == firstByte.Value);
        }
        catch (FormatException)
        {
            return false;
        }
    }

    private static string? Truncate(string? value, int maxLength)
    {
        var trimmed = value?.Trim();
        if (string.IsNullOrEmpty(trimmed))
            return null;

        return trimmed.Length <= maxLength ? trimmed : trimmed[..maxLength];
    }
}
