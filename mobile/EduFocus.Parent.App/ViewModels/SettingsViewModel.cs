using CommunityToolkit.Mvvm.ComponentModel;
using EduFocus.Parent.App.Services;
using EduFocus.Parent.Core;
using EduFocus.Parent.Core.Pairing;
using EduFocus.Parent.Core.Presentation;
using EduFocus.Parent.Core.Settings;

namespace EduFocus.Parent.App.ViewModels;

public sealed partial class SettingsViewModel(
    IPairingService pairing,
    AppState appState,
    ParentSettingsStore settings,
    PushPermissionService permission,
    PushCoordinator coordinator,
    UnifiedPushManager unifiedPush,
    IFamilyAdminService admin,
    ParentDataCache cache,
    SessionExpiryService session) : ObservableObject
{
    [ObservableProperty] private bool _isLinked;
    [ObservableProperty] private bool _isBusy;
    [ObservableProperty] private string _desktopName = string.Empty;
    [ObservableProperty] private string _serverUrl = string.Empty;
    [ObservableProperty] private string _serverHostText = "—";
    [ObservableProperty] private string _deviceNameText = string.Empty;
    [ObservableProperty] private string _platformText = string.Empty;
    [ObservableProperty] private string _appVersionText = string.Empty;
    [ObservableProperty] private string _connectionStatusText = string.Empty;
    [ObservableProperty] private bool _notificationsEnabled;
    [ObservableProperty] private bool _pushDistributorReady;
    [ObservableProperty] private IReadOnlyList<SettingsDeviceItem> _devices = [];
    [ObservableProperty] private bool _hasDevices;
    [ObservableProperty] private string _devicesStatus = string.Empty;
    [ObservableProperty] private bool _isLoadingDevices;
    [ObservableProperty] private bool _isDeletingCloudData;

    public event EventHandler? Unlinked;

    public bool IsNotBusy => !IsBusy;

    public bool ShowWelcomeHint => !IsLinked;

    public bool ShowEnableNotifications =>
        IsLinked && (!NotificationsEnabled || !PushDistributorReady);

    public string EnableNotificationsText => NotificationsEnabled && !PushDistributorReady
        ? "Kết nối lại thông báo"
        : "Bật thông báo";

    public bool HasDevicesStatus => DevicesStatus.Length > 0;

    public string NotificationStatusText
    {
        get
        {
            if (!NotificationsEnabled)
                return "Thông báo đang tắt. Ứng dụng vẫn xem được báo cáo khi mở.";

            return PushDistributorReady
                ? "Thông báo đang bật. Bạn sẽ nhận cảnh báo và báo cáo trên điện thoại."
                : "Quyền thông báo đã bật nhưng kết nối nền chưa chạy.";
        }
    }

    partial void OnIsBusyChanged(bool value) => OnPropertyChanged(nameof(IsNotBusy));

    partial void OnIsLinkedChanged(bool value)
    {
        OnPropertyChanged(nameof(ShowWelcomeHint));
        OnPropertyChanged(nameof(ShowEnableNotifications));
    }

    partial void OnNotificationsEnabledChanged(bool value)
    {
        OnPropertyChanged(nameof(ShowEnableNotifications));
        OnPropertyChanged(nameof(EnableNotificationsText));
        OnPropertyChanged(nameof(NotificationStatusText));
    }

    partial void OnPushDistributorReadyChanged(bool value)
    {
        OnPropertyChanged(nameof(ShowEnableNotifications));
        OnPropertyChanged(nameof(EnableNotificationsText));
        OnPropertyChanged(nameof(NotificationStatusText));
    }

    partial void OnDevicesStatusChanged(string value) =>
        OnPropertyChanged(nameof(HasDevicesStatus));

    public void Refresh()
    {
        IsLinked = appState.IsLinked;
        DesktopName = appState.DesktopName ?? settings.DesktopName ?? string.Empty;
        ServerUrl = appState.CloudBaseUrl ?? settings.CloudBaseUrl ?? string.Empty;
        ServerHostText = MaskServer(ServerUrl);
        DeviceNameText = string.IsNullOrWhiteSpace(DeviceInfo.Current.Name)
            ? "Điện thoại phụ huynh"
            : DeviceInfo.Current.Name;
        PlatformText = $"{DeviceInfo.Current.Platform} {DeviceInfo.Current.VersionString}";
        AppVersionText = AppInfo.Current.VersionString;
        ConnectionStatusText = IsLinked ? "Đã kết nối cloud" : "Chưa ghép nối";
        NotificationsEnabled = permission.IsGranted;
        PushDistributorReady = WssPushService.IsRunning
            || unifiedPush.GetAvailability() == UnifiedPushAvailability.Ready;

        if (!IsLinked)
        {
            Devices = [];
            HasDevices = false;
            DevicesStatus = string.Empty;
        }
    }

    public async Task<bool> EnableNotificationsAsync(Page page)
    {
        var granted = await coordinator.EnableNotificationsAsync(page);
        NotificationsEnabled = permission.IsGranted;
        PushDistributorReady = WssPushService.IsRunning
            || unifiedPush.GetAvailability() == UnifiedPushAvailability.Ready;
        return granted;
    }

    public async Task LoadDevicesAsync()
    {
        if (!IsLinked || IsLoadingDevices)
            return;

        IsLoadingDevices = true;
        DevicesStatus = string.Empty;
        try
        {
            var zone = FamilyCalendar.Resolve(appState.TimeZone);
            var devices = await admin.GetDevicesAsync();
            Devices = devices
                .Select(device => new SettingsDeviceItem(device, appState.DeviceId, zone))
                .ToList();
            HasDevices = Devices.Count > 0;
        }
        catch (Exception exception)
        {
            if (CloudErrors.IsUnauthorized(exception))
                await session.HandleUnauthorizedAsync();

            Devices = [];
            HasDevices = false;
            DevicesStatus = CloudErrors.Describe(exception);
        }
        finally
        {
            IsLoadingDevices = false;
        }
    }

    public async Task<bool> RevokeDeviceAsync(Guid deviceId)
    {
        if (IsBusy)
            return false;

        IsBusy = true;
        try
        {
            await admin.RevokeDeviceAsync(deviceId);
            await LoadDevicesAsync();
            return true;
        }
        catch (Exception exception)
        {
            if (CloudErrors.IsUnauthorized(exception))
                await session.HandleUnauthorizedAsync();

            DevicesStatus = CloudErrors.Describe(exception);
            return false;
        }
        finally
        {
            IsBusy = false;
        }
    }

    public async Task<bool> DeleteCloudDataAsync()
    {
        if (IsBusy || IsDeletingCloudData)
            return false;

        IsBusy = true;
        IsDeletingCloudData = true;
        try
        {
            await admin.DeleteFamilyAsync(FamilyAdminService.DeleteConfirmation);
            Refresh();
            return true;
        }
        catch (Exception exception)
        {
            if (CloudErrors.IsUnauthorized(exception))
                await session.HandleUnauthorizedAsync();

            return false;
        }
        finally
        {
            IsDeletingCloudData = false;
            IsBusy = false;
        }
    }

    public async Task<bool> UnlinkAsync()
    {
        if (IsBusy)
            return false;

        IsBusy = true;
        try
        {
            await pairing.UnlinkAsync();
            coordinator.OnUnlinked();
            cache.Clear();
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

    private static string MaskServer(string? url)
    {
        if (string.IsNullOrWhiteSpace(url))
            return "—";

        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri))
            return url;

        return uri.IsDefaultPort ? uri.Host : $"{uri.Host}:{uri.Port}";
    }
}
