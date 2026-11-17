using EduFocus.Parent.App.Services;
using EduFocus.Parent.Core.Presentation;

namespace EduFocus.Parent.App.Views;

public partial class NotificationsPage : ContentPage
{
    private readonly NotificationsViewModel _viewModel;

    public NotificationsPage()
    {
        InitializeComponent();
        _viewModel = ServiceHelper.GetService<NotificationsViewModel>();
        BindingContext = _viewModel;
    }

    protected override void OnAppearing()
    {
        base.OnAppearing();
        _ = _viewModel.LoadAsync();
    }
}
