using System.Diagnostics;
using EduFocus.Parent.App.Services;
using EduFocus.Parent.Core.Pairing;

namespace EduFocus.Parent.App;

public partial class AppShell : Shell
{
    private bool _initialized;

    public AppShell()
    {
        InitializeComponent();
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        if (_initialized)
            return;

        _initialized = true;
        try
        {
            var linked = await ServiceHelper.GetService<LinkStateRestorer>().RestoreAsync();
            if (!linked)
                await GoToAsync("//welcome", animate: false);

            PairingDeepLink.Received += OnDeepLinkReceived;

            if (PairingDeepLink.ConsumePending() is { } pending)
                await OpenPairingAsync(pending);
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Startup routing failed: {ex.GetType().Name}");
        }
    }

    private void OnDeepLinkReceived(string uri) =>
        MainThread.BeginInvokeOnMainThread(async () =>
        {
            if (!_initialized)
                return;

            try
            {
                PairingDeepLink.ConsumePending();
                await OpenPairingAsync(uri);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Deep link routing failed: {ex.GetType().Name}");
            }
        });

    private Task OpenPairingAsync(string uri) =>
        GoToAsync($"//pairing?data={Uri.EscapeDataString(uri)}", animate: false);
}
