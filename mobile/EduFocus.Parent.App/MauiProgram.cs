using CommunityToolkit.Maui;
using EduFocus.Parent.App.Services;
using EduFocus.Parent.App.ViewModels;
using EduFocus.Parent.Core;
using EduFocus.Parent.Core.Pairing;
using EduFocus.Parent.Core.Presentation;
using EduFocus.Parent.Core.Push;
using EduFocus.Parent.Core.Services;
using EduFocus.Parent.Core.Settings;
using ZXing.Net.Maui.Controls;

namespace EduFocus.Parent.App;

public static class MauiProgram
{
    public static MauiApp CreateMauiApp()
    {
        var builder = MauiApp.CreateBuilder();
        builder
            .UseMauiApp<App>()
            .UseMauiCommunityToolkit()
            .UseBarcodeReader();

        builder.Services.AddSingleton<AppState>();
        builder.Services.AddSingleton<ITokenStore, SecureStorageTokenStore>();
        builder.Services.AddSingleton<IKeyValueStore, PreferencesKeyValueStore>();
        builder.Services.AddSingleton<ParentSettingsStore>();
        builder.Services.AddHttpClient(CloudApiClient.HttpClientName, client =>
        {
            client.Timeout = TimeSpan.FromSeconds(15);
            client.DefaultRequestHeaders.UserAgent.ParseAdd("EduFocusParent/0.1");
        });
        builder.Services.AddSingleton<ICloudApiClient, CloudApiClient>();
        builder.Services.AddSingleton<IPairingService>(sp => new PairingService(
            sp.GetRequiredService<ICloudApiClient>(),
            sp.GetRequiredService<ITokenStore>(),
            sp.GetRequiredService<AppState>(),
            sp.GetRequiredService<ParentSettingsStore>(),
            BuildConfig.AllowInsecureCloudEndpoints));
        builder.Services.AddSingleton<LinkStateRestorer>();
        builder.Services.AddSingleton<PairingViewModel>();
        builder.Services.AddSingleton<SettingsViewModel>();

        builder.Services.AddSingleton<ParentDataCache>();
        builder.Services.AddSingleton<SessionExpiryService>();
        builder.Services.AddSingleton<IParentDataService, ParentDataService>();
        builder.Services.AddSingleton<IFamilyAdminService, FamilyAdminService>();
        builder.Services.AddSingleton(sp => new OverviewViewModel(
            sp.GetRequiredService<IParentDataService>(),
            sp.GetRequiredService<ParentDataCache>(),
            sp.GetRequiredService<AppState>(),
            sp.GetRequiredService<SessionExpiryService>()));
        builder.Services.AddSingleton(sp => new ReportsViewModel(
            sp.GetRequiredService<IParentDataService>(),
            sp.GetRequiredService<ParentDataCache>(),
            sp.GetRequiredService<AppState>(),
            sp.GetRequiredService<SessionExpiryService>()));
        builder.Services.AddSingleton(sp => new NotificationsViewModel(
            sp.GetRequiredService<IParentDataService>(),
            sp.GetRequiredService<ParentDataCache>(),
            sp.GetRequiredService<AppState>(),
            sp.GetRequiredService<SessionExpiryService>()));

        builder.Services.AddSingleton<PushNotificationDedupe>();
        builder.Services.AddSingleton<IPushRegistrationService, PushRegistrationService>();
        builder.Services.AddSingleton<PushNotificationPresenter>();
        builder.Services.AddSingleton<PushPermissionService>();
        builder.Services.AddSingleton<PushCoordinator>();
#if FCM_ENABLED
        builder.Services.AddSingleton<IPushTokenProvider, FirebasePushTokenProvider>();
        builder.Services.AddSingleton<PushMessageHandler>();
#else
        builder.Services.AddSingleton<IPushTokenProvider, DisabledPushTokenProvider>();
#endif

        var app = builder.Build();

#if FCM_ENABLED
        FirebasePushBootstrap.Initialize(app.Services);
#endif

        return app;
    }
}

internal static class BuildConfig
{
#if DEBUG
    public const bool AllowInsecureCloudEndpoints = true;
#else
    public const bool AllowInsecureCloudEndpoints = false;
#endif
}
