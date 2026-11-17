using Android.Content.PM;
using AndroidX.Core.App;
using AndroidX.Core.Content;
using Application = Android.App.Application;

namespace EduFocus.Parent.App.Services;

/// <summary>
/// Android 13+ requires POST_NOTIFICATIONS; older versions only need the
/// channel and the app-level notification toggle.
/// </summary>
public sealed class PushPermissionService
{
    public bool IsGranted
    {
        get
        {
            var context = Application.Context;
            if (OperatingSystem.IsAndroidVersionAtLeast(33)
                && ContextCompat.CheckSelfPermission(context, Android.Manifest.Permission.PostNotifications)
                    != Permission.Granted)
            {
                return false;
            }

            return NotificationManagerCompat.From(context)?.AreNotificationsEnabled() ?? false;
        }
    }

    public async Task<bool> RequestAsync()
    {
        if (!OperatingSystem.IsAndroidVersionAtLeast(33))
            return IsGranted;

        var status = await Permissions.RequestAsync<Permissions.PostNotifications>();
        return status == PermissionStatus.Granted;
    }

    public void OpenSettings() => AppInfo.Current.ShowSettingsUI();
}
