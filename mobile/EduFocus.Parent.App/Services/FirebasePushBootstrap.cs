#if FCM_ENABLED
using Plugin.Firebase.CloudMessaging;
using Plugin.Firebase.CloudMessaging.Platforms.Android;

namespace EduFocus.Parent.App.Services;

/// <summary>
/// Wires the plugin into the app: suppresses its default notification so our
/// safe presenter owns display, creates the alert channel and forwards FCM
/// events to the singleton handler. Runs inside Application.OnCreate, so it is
/// active even when a data message starts a killed process.
/// </summary>
public static class FirebasePushBootstrap
{
    public static void Initialize(IServiceProvider services)
    {
        FirebaseCloudMessagingImplementation.ChannelId = PushNotificationPresenter.ChannelId;
        FirebaseCloudMessagingImplementation.SmallIconRef = Resource.Mipmap.appicon;
        FirebaseCloudMessagingImplementation.ShowLocalNotificationAction = _ => { };

        services.GetRequiredService<PushNotificationPresenter>().EnsureChannel();

        var handler = services.GetRequiredService<PushMessageHandler>();
        var messaging = CrossFirebaseCloudMessaging.Current;
        messaging.NotificationReceived += handler.OnNotificationReceived;
        messaging.TokenChanged += handler.OnTokenChanged;
    }
}
#endif
