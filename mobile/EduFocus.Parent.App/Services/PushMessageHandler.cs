#if FCM_ENABLED
using EduFocus.Parent.Core.Push;
using EduFocus.Parent.Core.Settings;
using Plugin.Firebase.CloudMessaging;
using Plugin.Firebase.CloudMessaging.EventArgs;

namespace EduFocus.Parent.App.Services;

/// <summary>
/// Entry point for plugin-delivered data messages. Messages are parsed and
/// deduplicated before display; unknown types and repeated notification ids
/// are dropped silently.
/// </summary>
public sealed class PushMessageHandler(
    PushNotificationDedupe dedupe,
    PushNotificationPresenter presenter,
    PushCoordinator coordinator,
    ParentSettingsStore settings)
{
    public void OnNotificationReceived(object? sender, FCMNotificationReceivedEventArgs e)
    {
        if (settings.DeviceId is null)
            return;

        if (e.Notification?.Data is not { } data)
            return;

        var payloadData = new Dictionary<string, string>(data, StringComparer.Ordinal);
        if (!PushNotificationPayloadParser.TryParse(payloadData, out var payload) || payload is null)
            return;

        if (!dedupe.TryRemember(payload.DedupeKey))
            return;

        presenter.Present(payload, AppLifecycleState.IsForeground);
    }

    public void OnTokenChanged(object? sender, FCMTokenChangedEventArgs e) =>
        _ = MainThread.InvokeOnMainThreadAsync(() => coordinator.SyncTokenAsync(force: true));
}
#endif
