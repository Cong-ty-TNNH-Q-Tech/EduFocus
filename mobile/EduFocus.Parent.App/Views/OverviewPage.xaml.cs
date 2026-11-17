using EduFocus.Parent.App.Services;
using EduFocus.Parent.Core.Presentation;

namespace EduFocus.Parent.App.Views;

public partial class OverviewPage : ContentPage
{
    private readonly OverviewViewModel _viewModel;

    public OverviewPage()
    {
        InitializeComponent();
        _viewModel = ServiceHelper.GetService<OverviewViewModel>();
        BindingContext = _viewModel;
    }

    protected override void OnAppearing()
    {
        base.OnAppearing();
        _ = _viewModel.LoadAsync();
    }
}
