using Android.App;
using Android.Content;
using EduFocus.Parent.App.Services;
using EduFocus.Parent.Core.Settings;
using Debug = System.Diagnostics.Debug;

namespace EduFocus.Parent.App;

/// <summary>
/// Restarts the foreground WebSocket push service after device reboot or app
/// update when the family is linked and WebSocket delivery is active.
/// </summary>
[BroadcastReceiver(Enabled = true, Exported = true, DirectBootAware = false)]
[IntentFilter(new[] { Intent.ActionBootCompleted, Intent.ActionMyPackageReplaced })]
public sealed class WssBootReceiver : BroadcastReceiver
{
    public override void OnReceive(Context? context, Intent? intent)
    {
        var action = intent?.Action;
        if (!string.Equals(action, Intent.ActionBootCompleted, StringComparison.Ordinal)
            && !string.Equals(action, Intent.ActionMyPackageReplaced, StringComparison.Ordinal))
        {
            return;
        }

        try
        {
            var settings = new ParentSettingsStore(new PreferencesKeyValueStore());
            if (settings.DeviceId is null
                || string.IsNullOrWhiteSpace(settings.CloudBaseUrl)
                || !settings.PushWebSocketEnabled
                || !new PushPermissionService().IsGranted)
            {
                return;
            }

            WssPushService.Start();
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Boot receiver skipped push service start: {ex.GetType().Name}");
        }
    }
}
