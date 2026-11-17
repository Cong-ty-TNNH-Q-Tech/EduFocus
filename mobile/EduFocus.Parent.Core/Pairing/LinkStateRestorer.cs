using EduFocus.Parent.Core.Services;
using EduFocus.Parent.Core.Settings;

namespace EduFocus.Parent.Core.Pairing;

/// <summary>
/// Restores the linked state at app start from SecureStorage + preferences.
/// A token without link metadata (or the reverse) is treated as a partial
/// link and cleared so the app falls back to the welcome screen.
/// </summary>
public sealed class LinkStateRestorer(
    ITokenStore tokenStore,
    ParentSettingsStore settings,
    AppState appState)
{
    public async Task<bool> RestoreAsync(CancellationToken cancellationToken = default)
    {
        var token = await tokenStore.GetTokenAsync(cancellationToken);
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

        if (!string.IsNullOrWhiteSpace(token))
            await tokenStore.ClearAsync(cancellationToken);

        settings.ClearLink();
        appState.IsLinked = false;
        appState.FamilyId = null;
        appState.DeviceId = null;
        appState.TimeZone = null;
        appState.DesktopName = null;
        return false;
    }
}
