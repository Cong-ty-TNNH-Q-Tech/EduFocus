using EduFocus.Parent.Core.Services;
using EduFocus.Parent.Core.Settings;

namespace EduFocus.Parent.Core.Presentation;

/// <summary>
/// Clears the local link when the cloud rejects the token (401) and raises a
/// single event so the shell can send the user back to the welcome screen.
/// </summary>
public sealed class SessionExpiryService(
    ITokenStore tokenStore,
    AppState appState,
    ParentSettingsStore settings,
    ParentDataCache cache)
{
    private int _expired;

    public event Action? Expired;

    public async Task ClearLocalAsync(CancellationToken cancellationToken = default)
    {
        await tokenStore.ClearAsync(cancellationToken);
        appState.IsLinked = false;
        appState.FamilyId = null;
        appState.DeviceId = null;
        appState.TimeZone = null;
        appState.DesktopName = null;
        settings.ClearLink();
        cache.Clear();
    }

    public async Task HandleUnauthorizedAsync(CancellationToken cancellationToken = default)
    {
        if (Interlocked.Exchange(ref _expired, 1) == 1)
            return;

        await ClearLocalAsync(cancellationToken);
        Expired?.Invoke();
    }

    public void Reset() => Interlocked.Exchange(ref _expired, 0);
}
