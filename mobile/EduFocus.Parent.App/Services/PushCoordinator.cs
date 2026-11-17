using System.Diagnostics;
using EduFocus.Parent.Core;
using EduFocus.Parent.Core.Push;

namespace EduFocus.Parent.App.Services;

/// <summary>
/// Ties the FCM token to the pair token: registers after pairing, after a
/// token refresh and when Settings. Never throws into the UI and never writes
/// the FCM token anywhere except the registration request.
/// </summary>
public sealed class PushCoordinator(
    IPushRegistrationService registration,
    IPushTokenProvider tokenProvider,
    PushPermissionService permission,
    AppState appState)
{
    public const string PlatformName = "android";

    public async Task SyncTokenAsync(bool force = false, CancellationToken cancellationToken = default)
    {
        if (!appState.IsLinked)
            return;

        try
        {
            var token = await tokenProvider.GetTokenAsync(cancellationToken);
            await registration.RegisterAsync(
                token,
                PlatformName,
                AppInfo.Current.VersionString,
                force,
                cancellationToken);
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Push token sync skipped: {ex.GetType().Name}");
        }
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
}
