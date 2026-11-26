using System.Diagnostics;
using EduFocus.Cloud.Contracts.Push;
using EduFocus.Parent.Core.Presentation;
using EduFocus.Parent.Core.Services;

namespace EduFocus.Parent.Core.Push;

public interface IPushConfigService
{
    Task<PushConfigResponse?> GetAsync(CancellationToken cancellationToken = default);
}

/// <summary>
/// Fetches the application server's public Web Push settings. The VAPID public
/// key is not a secret, but it must match the backend key so the distributor
/// binds the endpoint to the right application server.
/// </summary>
public sealed class PushConfigService(
    ICloudApiClient api,
    SessionExpiryService? sessionExpiry = null) : IPushConfigService
{
    private const string ConfigPath = "/v1/push/config";

    public async Task<PushConfigResponse?> GetAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            return await api.GetAsync<PushConfigResponse>(
                ConfigPath,
                cancellationToken: cancellationToken);
        }
        catch (CloudApiException ex) when (ex.StatusCode == 401)
        {
            if (sessionExpiry is not null)
                await sessionExpiry.HandleUnauthorizedAsync(cancellationToken);

            return null;
        }
        catch (Exception ex) when (ex is CloudApiException or HttpRequestException or TaskCanceledException)
        {
            Debug.WriteLine($"Push config fetch skipped: {ex.GetType().Name}");
            return null;
        }
    }
}
