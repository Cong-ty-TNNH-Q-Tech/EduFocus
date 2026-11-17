using Android.App;
using Android.Content;
using Android.Content.PM;
using Android.OS;
using EduFocus.Parent.App.Services;
#if FCM_ENABLED
using Plugin.Firebase.Core.Platforms.Android;
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

#if FCM_ENABLED
        CrossFirebase.Initialize(this, () => Microsoft.Maui.ApplicationModel.Platform.CurrentActivity ?? this);
#endif

        PairingDeepLink.Handle(Intent?.DataString);
        HandlePushIntent(Intent);
    }

    protected override void OnNewIntent(Intent? intent)
    {
        base.OnNewIntent(intent);
        PairingDeepLink.Handle(intent?.DataString);
        HandlePushIntent(intent);
    }

    private static void HandlePushIntent(Intent? intent) =>
        PushTapRouter.Handle(
            intent?.GetStringExtra(PushTapRouter.TypeExtra),
            intent?.GetStringExtra(PushTapRouter.DateExtra));
}
