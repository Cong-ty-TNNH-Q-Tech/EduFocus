using CommunityToolkit.Maui;
using EduFocus.Parent.App.Services;
using EduFocus.Parent.App.ViewModels;
using EduFocus.Parent.Core;
using EduFocus.Parent.Core.Pairing;
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

        return builder.Build();
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
