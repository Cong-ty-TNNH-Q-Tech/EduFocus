using CommunityToolkit.Mvvm.ComponentModel;
using EduFocus.Parent.App.Services;
using EduFocus.Parent.Core;
using EduFocus.Parent.Core.Pairing;
using EduFocus.Parent.Core.Settings;

namespace EduFocus.Parent.App.ViewModels;

public sealed partial class SettingsViewModel(
    IPairingService pairing,
    AppState appState,
    ParentSettingsStore settings,
    PushPermissionService permission,
    PushCoordinator coordinator) : ObservableObject
{
    [ObservableProperty] private bool _isLinked;
    [ObservableProperty] private bool _isBusy;
    [ObservableProperty] private string _desktopName = string.Empty;
    [ObservableProperty] private string _serverUrl = string.Empty;
    [ObservableProperty] private bool _notificationsEnabled;

    public event EventHandler? Unlinked;

    public bool IsNotBusy => !IsBusy;

    public bool ShowWelcomeHint => !IsLinked;

    public bool ShowEnableNotifications => IsLinked && !NotificationsEnabled;

    public string NotificationStatusText => NotificationsEnabled
        ? "Thông báo đang bật. Bạn sẽ nhận cảnh báo và báo cáo trên điện thoại."
        : "Thông báo đang tắt. Ứng dụng vẫn xem được báo cáo khi mở.";

    partial void OnIsBusyChanged(bool value) => OnPropertyChanged(nameof(IsNotBusy));

    partial void OnIsLinkedChanged(bool value)
    {
        OnPropertyChanged(nameof(ShowWelcomeHint));
        OnPropertyChanged(nameof(ShowEnableNotifications));
    }

    partial void OnNotificationsEnabledChanged(bool value)
    {
        OnPropertyChanged(nameof(ShowEnableNotifications));
        OnPropertyChanged(nameof(NotificationStatusText));
    }

    public void Refresh()
    {
        IsLinked = appState.IsLinked;
        DesktopName = appState.DesktopName ?? settings.DesktopName ?? string.Empty;
        ServerUrl = appState.CloudBaseUrl ?? settings.CloudBaseUrl ?? string.Empty;
        NotificationsEnabled = permission.IsGranted;
    }

    public async Task<bool> EnableNotificationsAsync(Page page)
    {
        var granted = await coordinator.RequestPermissionAsync(page);
        NotificationsEnabled = permission.IsGranted;
        if (NotificationsEnabled)
            _ = coordinator.SyncTokenAsync(force: true);

        return granted;
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
