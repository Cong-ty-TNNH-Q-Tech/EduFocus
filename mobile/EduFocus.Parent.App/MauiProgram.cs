using CommunityToolkit.Maui;
using EduFocus.Parent.App.Services;
using EduFocus.Parent.Core;
using EduFocus.Parent.Core.Services;

namespace EduFocus.Parent.App;

public static class MauiProgram
{
    public static MauiApp CreateMauiApp()
    {
        var builder = MauiApp.CreateBuilder();
        builder
            .UseMauiApp<App>()
            .UseMauiCommunityToolkit();

        builder.Services.AddSingleton<AppState>();
        builder.Services.AddSingleton<ITokenStore, SecureStorageTokenStore>();
        builder.Services.AddHttpClient(CloudApiClient.HttpClientName, client =>
        {
            client.Timeout = TimeSpan.FromSeconds(15);
            client.DefaultRequestHeaders.UserAgent.ParseAdd("EduFocusParent/0.1");
        });
        builder.Services.AddSingleton<ICloudApiClient, CloudApiClient>();

        return builder.Build();
    }
}
