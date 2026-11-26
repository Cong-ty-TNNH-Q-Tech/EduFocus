using EduFocus.Parent.App.Services;
using EduFocus.Parent.Core.Presentation;
using EduFocus.Parent.Core.Push;

namespace EduFocus.Parent.App.Views;

public partial class OverviewPage : ContentPage
{
    private readonly OverviewViewModel _viewModel;
    private readonly PushEventHub _pushEvents;

    public OverviewPage()
    {
        InitializeComponent();
        _viewModel = ServiceHelper.GetService<OverviewViewModel>();
        _pushEvents = ServiceHelper.GetService<PushEventHub>();
        BindingContext = _viewModel;
    }

    protected override void OnAppearing()
    {
        base.OnAppearing();
        _pushEvents.Received += OnPushReceived;
        _ = _viewModel.LoadAsync();
    }

    protected override void OnDisappearing()
    {
        _pushEvents.Received -= OnPushReceived;
        base.OnDisappearing();
    }

    private void OnPushReceived(PushNotificationPayload payload) =>
        MainThread.BeginInvokeOnMainThread(() => _ = _viewModel.LoadAsync());
}
