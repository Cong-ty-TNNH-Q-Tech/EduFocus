using System.Diagnostics;
using EduFocus.Parent.Core;
using EduFocus.Parent.Core.Pairing;
using EduFocus.Parent.Core.Push;
using EduFocus.Parent.Core.Settings;

namespace EduFocus.Parent.App.Services;

/// <summary>
/// Ties the UnifiedPush endpoint to the pair token: registers after pairing,
/// after an endpoint change and when Settings opens. Never throws into the UI
/// and never writes the endpoint anywhere except the registration request and
/// local preferences.
/// </summary>
public sealed class PushCoordinator(
    IPushRegistrationService registration,
    IPushConfigService pushConfig,
    ParentSettingsStore settings,
    UnifiedPushManager unifiedPush,
    PushPermissionService permission,
    LinkStateRestorer restorer,
    AppState appState)
{
    public const string PlatformName = "android";

    public async Task SyncEndpointAsync(bool force = false, CancellationToken cancellationToken = default)
    {
        try
        {
            if (!appState.IsLinked)
                await restorer.RestoreAsync(cancellationToken);

            if (!appState.IsLinked)
                return;

            var config = await ResolvePushConfigAsync(cancellationToken);
            if (!config.Enabled)
                return;

            var endpoint = settings.GetPushEndpoint();
            if (endpoint is null)
            {
                unifiedPush.EnsureRegistered(config.VapidPublicKey);
                return;
            }

            await registration.RegisterAsync(
                endpoint,
                PlatformName,
                AppInfo.Current.VersionString,
                force,
                cancellationToken);
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Push endpoint sync skipped: {ex.GetType().Name}");
        }
    }

    public void OnEndpointChanged(PushEndpointRegistration endpoint)
    {
        settings.SetPushEndpoint(endpoint);
        _ = SyncEndpointAsync(force: true);
    }

    public void OnRegistrationFailed()
    {
        settings.ClearPushEndpoint();
    }

    public void OnUnregistered()
    {
        settings.ClearPushEndpoint();
    }

    public void OnUnlinked()
    {
        unifiedPush.Unregister();
        settings.ClearPushEndpoint();
    }

    public async Task<bool> EnableNotificationsAsync(Page page)
    {
        if (!await RequestPermissionAsync(page))
            return false;

        var config = await ResolvePushConfigAsync();
        if (!config.Enabled)
        {
            await page.DisplayAlertAsync(
                "Máy chủ chưa bật thông báo",
                "Máy chủ EduFocus hiện không gửi thông báo đẩy. Bạn vẫn xem được báo cáo khi mở ứng dụng.",
                "OK");
            return false;
        }

        if (!await EnsureDistributorAsync(page, config.VapidPublicKey))
            return false;

        await SyncEndpointAsync(force: true);
        return true;
    }

    public async Task<bool> RequestPermissionAsync(Page page)
    {
        if (permission.IsGranted)
            return true;

        var allow = await page.DisplayAlertAsync(
            "Bật thông báo?",
            "EduFocus cần quyền thông báo để gửi cảnh báo và báo cáo đến điện thoại. Bạn có thể tắt lại bất cứ lúc nào trong Cài đặt.",
            "Bật",
            "Để sau");
        if (!allow)
            return false;

        var granted = await permission.RequestAsync();
        if (!granted)
        {
            await page.DisplayAlertAsync(
                "Chưa bật thông báo",
                "Bạn vẫn xem được báo cáo khi mở ứng dụng. Có thể bật lại trong phần Cài đặt của ứng dụng.",
                "OK");
        }

        return granted;
    }

    private async Task<(bool Enabled, string? VapidPublicKey)> ResolvePushConfigAsync(
        CancellationToken cancellationToken = default)
    {
        var config = await pushConfig.GetAsync(cancellationToken);
        if (config is { Enabled: false })
            return (false, null);

        var vapid = config?.VapidPublicKey;
        if (string.IsNullOrWhiteSpace(vapid))
            vapid = settings.PushVapidPublicKey;

        if (!string.IsNullOrWhiteSpace(vapid))
            settings.PushVapidPublicKey = vapid;

        return (true, vapid);
    }

    private async Task<bool> EnsureDistributorAsync(Page page, string? vapidPublicKey)
    {
        switch (unifiedPush.GetAvailability())
        {
            case UnifiedPushAvailability.Ready:
                unifiedPush.EnsureRegistered(vapidPublicKey);
                return true;

            case UnifiedPushAvailability.NeedsSelection:
                if (await unifiedPush.RequestDistributorAsync(vapidPublicKey))
                    return true;

                await page.DisplayAlertAsync(
                    "Chưa chọn ứng dụng thông báo",
                    "EduFocus cần một ứng dụng phân phối UnifiedPush như ntfy để nhận thông báo. Hãy cài ntfy, sau đó chọn nó làm trình phân phối mặc định.",
                    "OK");
                return false;

            default:
                await page.DisplayAlertAsync(
                    "Cần ứng dụng thông báo",
                    "EduFocus không dùng Google Play Services. Hãy cài ntfy (F-Droid hoặc bản tự lưu trữ) và chọn ntfy làm trình phân phối UnifiedPush mặc định.",
                    "OK");
                return false;
        }
    }
}
