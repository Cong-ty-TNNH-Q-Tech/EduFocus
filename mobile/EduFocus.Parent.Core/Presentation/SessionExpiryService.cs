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
    private readonly object _gate = new();
    private Task? _clearingTask;

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
        Task clearingTask;
        lock (_gate)
        {
            if (_clearingTask is null || _clearingTask.IsFaulted)
            {
                _clearingTask = RunClearAsync();
            }

            clearingTask = _clearingTask;
        }

        try
        {
            await clearingTask.WaitAsync(cancellationToken);
        }
        catch
        {
            if (clearingTask.IsFaulted)
            {
                lock (_gate)
                {
                    if (ReferenceEquals(_clearingTask, clearingTask))
                    {
                        _clearingTask = null;
                    }
                }
            }

            throw;
        }
    }

    public void Reset()
    {
        lock (_gate)
        {
            _clearingTask = null;
        }
    }

    private async Task RunClearAsync()
    {
        await ClearLocalAsync(CancellationToken.None);
        Expired?.Invoke();
    }
}
