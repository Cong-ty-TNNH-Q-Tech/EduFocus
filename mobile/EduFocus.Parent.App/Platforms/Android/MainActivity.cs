using Android.App;
using Android.Content;
using Android.Content.PM;
using Android.OS;
using EduFocus.Parent.App.Services;
#if DEBUG
using EduFocus.Parent.Core.Push;
#endif

namespace EduFocus.Parent.App;

[Activity(
    Theme = "@style/Maui.SplashTheme",
    MainLauncher = true,
    LaunchMode = LaunchMode.SingleTop,
    ScreenOrientation = ScreenOrientation.Portrait,
    ConfigurationChanges = ConfigChanges.ScreenSize
        | ConfigChanges.Orientation
        | ConfigChanges.UiMode
        | ConfigChanges.ScreenLayout
        | ConfigChanges.SmallestScreenSize
        | ConfigChanges.Density)]
[IntentFilter(
    new[] { Intent.ActionView },
    Categories = new[] { Intent.CategoryDefault, Intent.CategoryBrowsable },
    DataScheme = "edufocus",
    DataHost = "pair")]
public class MainActivity : MauiAppCompatActivity
{
    protected override void OnCreate(Bundle? savedInstanceState)
    {
        base.OnCreate(savedInstanceState);

        if (savedInstanceState is null
            && (Intent?.Flags.HasFlag(ActivityFlags.LaunchedFromHistory) != true))
        {
            PairingDeepLink.Handle(Intent?.DataString);
            HandlePushIntent(Intent);
#if DEBUG
            HandleDemoPush(Intent);
#endif
        }
    }

    protected override void OnNewIntent(Intent? intent)
    {
        base.OnNewIntent(intent);
        PairingDeepLink.Handle(intent?.DataString);
        HandlePushIntent(intent);
#if DEBUG
        HandleDemoPush(intent);
#endif
    }

    private static void HandlePushIntent(Intent? intent) =>
        PushTapRouter.Handle(
            intent?.GetStringExtra(PushTapRouter.TypeExtra),
            intent?.GetStringExtra(PushTapRouter.DateExtra));

#if DEBUG
    private const string DemoPushExtra = "edufocus.debug.push";

    private void HandleDemoPush(Intent? intent)
    {
        if (intent?.GetStringExtra(DemoPushExtra) is not { } typeKey
            || !PushNotificationPayloadParser.TryParseType(typeKey, out var type))
        {
            return;
        }

        var typeName = PushNotificationPayloadParser.TypeKeyOf(type);
        var presenter = ServiceHelper.GetService<PushNotificationPresenter>();
        presenter.EnsureChannel();
        presenter.Present(
            new PushNotificationPayload(
                type,
                typeName,
                DateOnly.FromDateTime(DateTime.Today),
                $"{typeName}:demo-{Guid.NewGuid():N}"),
            foreground: false);
    }
#endif
}
