using EduFocus.Parent.Core.Presentation;
using EduFocus.Parent.Core.Services;
using EduFocus.Parent.Core.Settings;

namespace EduFocus.Parent.Core.Pairing;

/// <summary>
/// Restores the linked state at app start from SecureStorage + preferences.
/// A token without link metadata (or the reverse) is treated as a partial
/// link and cleared so the app falls back to the welcome screen. Android may
/// restore SecureStorage ciphertext without the Keystore key, so an unreadable
/// token is handled like a missing token: everything is reset and the phone
/// must pair again.
/// </summary>
public sealed class LinkStateRestorer(
    ITokenStore tokenStore,
    ParentSettingsStore settings,
    ParentDataCache cache,
    AppState appState)
{
    public async Task<bool> RestoreAsync(CancellationToken cancellationToken = default)
    {
        settings.RemoveLegacyPushToken();

        string? token;
        try
        {
            token = await tokenStore.GetTokenAsync(cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception)
        {
            await ResetAsync(cancellationToken);
            return false;
        }

        var cloudBaseUrl = settings.CloudBaseUrl;
        var familyId = settings.FamilyId;
        var deviceId = settings.DeviceId;

        appState.CloudBaseUrl = cloudBaseUrl;

        if (!string.IsNullOrWhiteSpace(token)
            && !string.IsNullOrWhiteSpace(cloudBaseUrl)
            && familyId is not null
            && deviceId is not null)
        {
            appState.IsLinked = true;
            appState.FamilyId = familyId;
            appState.DeviceId = deviceId;
            appState.TimeZone = settings.TimeZoneId;
            appState.DesktopName = settings.DesktopName;
            return true;
        }

        await ResetAsync(cancellationToken);
        return false;
    }

    private async Task ResetAsync(CancellationToken cancellationToken)
    {
        try
        {
            await tokenStore.ClearAsync(cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception)
        {
            // The stored token is already unusable; settings and cache still go.
        }

        settings.ClearLink();
        cache.Clear();
        appState.CloudBaseUrl = settings.CloudBaseUrl;
        appState.IsLinked = false;
        appState.FamilyId = null;
        appState.DeviceId = null;
        appState.TimeZone = null;
        appState.DesktopName = null;
    }
}
