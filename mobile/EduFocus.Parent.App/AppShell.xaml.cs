using System.Diagnostics;
using EduFocus.Parent.App.Services;
using EduFocus.Parent.Core;
using EduFocus.Parent.Core.Pairing;
using EduFocus.Parent.Core.Presentation;

namespace EduFocus.Parent.App;

public partial class AppShell : Shell
{
    private bool _initialized;
    private bool _initializing;

    public AppShell()
    {
        InitializeComponent();
        Routing.RegisterRoute("privacy", typeof(Views.PrivacyPage));
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        if (_initialized || _initializing)
            return;

        _initializing = true;

        var linked = false;
        try
        {
            linked = await ServiceHelper.GetService<LinkStateRestorer>().RestoreAsync();
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Startup restore failed: {ex.GetType().Name}");
        }

        // Subscriptions must happen even when restore fails, otherwise deep
        // links and notification taps stay dead for the rest of the session.
        try
        {
            PairingDeepLink.Received += OnDeepLinkReceived;
            PushTapRouter.Tapped += OnPushTapped;
            ServiceHelper.GetService<SessionExpiryService>().Expired += OnSessionExpired;
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Startup subscriptions failed: {ex.GetType().Name}");
        }
        finally
        {
            _initializing = false;
            _initialized = true;
        }

        try
        {
            if (!linked)
            {
                await GoToAsync("//welcome", animate: false);
                PushTapRouter.ConsumePending();
            }
            else
            {
                _ = ServiceHelper.GetService<PushCoordinator>().SyncEndpointAsync();

                if (PushTapRouter.ConsumePending() is { } route)
                    await GoToAsync(route, animate: false);
                else
                    await ServiceHelper.GetService<OverviewViewModel>().LoadAsync();
            }

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

    private void OnPushTapped(string route) =>
        MainThread.BeginInvokeOnMainThread(async () =>
        {
            try
            {
                var state = ServiceHelper.GetService<AppState>();
                if (!state.IsLinked)
                    return;

                await GoToAsync(route, animate: false);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Push routing failed: {ex.GetType().Name}");
            }
        });

    private void OnSessionExpired() =>
        MainThread.BeginInvokeOnMainThread(async () =>
        {
            try
            {
                await GoToAsync("//welcome", animate: false);
                await DisplayAlertAsync(
                    "Phiên ghép nối đã hết",
                    "Mã truy cập cloud không còn hiệu lực. Vui lòng ghép nối lại với máy tính để tiếp tục.",
                    "OK");
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Session expiry routing failed: {ex.GetType().Name}");
            }
        });

    private Task OpenPairingAsync(string uri) =>
        GoToAsync($"//pairing?data={Uri.EscapeDataString(uri)}", animate: false);
}
