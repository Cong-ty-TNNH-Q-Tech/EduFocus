using CommunityToolkit.Mvvm.ComponentModel;
using EduFocus.Parent.Core;
using EduFocus.Parent.Core.Pairing;
using EduFocus.Parent.Core.Settings;

namespace EduFocus.Parent.App.ViewModels;

public sealed partial class SettingsViewModel(
    IPairingService pairing,
    AppState appState,
    ParentSettingsStore settings) : ObservableObject
{
    [ObservableProperty] private bool _isLinked;
    [ObservableProperty] private bool _isBusy;
    [ObservableProperty] private string _desktopName = string.Empty;
    [ObservableProperty] private string _serverUrl = string.Empty;

    public event EventHandler? Unlinked;

    public bool IsNotBusy => !IsBusy;

    public bool ShowWelcomeHint => !IsLinked;

    partial void OnIsBusyChanged(bool value) => OnPropertyChanged(nameof(IsNotBusy));

    partial void OnIsLinkedChanged(bool value) => OnPropertyChanged(nameof(ShowWelcomeHint));

    public void Refresh()
    {
        IsLinked = appState.IsLinked;
        DesktopName = appState.DesktopName ?? settings.DesktopName ?? string.Empty;
        ServerUrl = appState.CloudBaseUrl ?? settings.CloudBaseUrl ?? string.Empty;
    }

    public async Task<bool> UnlinkAsync()
    {
        if (IsBusy)
            return false;

        IsBusy = true;
        try
        {
            await pairing.UnlinkAsync();
            Refresh();
            Unlinked?.Invoke(this, EventArgs.Empty);
            return true;
        }
        catch (Exception)
        {
            return false;
        }
        finally
        {
            IsBusy = false;
        }
    }
}
