using MaterialDesignThemes.Wpf;
using System.Windows.Controls;
using EduFocus.WPF.ViewModels;
using Microsoft.Extensions.DependencyInjection;

namespace EduFocus.WPF.Views;

public partial class FocusSessionView : UserControl
{
    public FocusSessionView()
    {
        InitializeComponent();
        DataContext = App.Services.GetRequiredService<FocusSessionViewModel>();
    }

    private async void UserControl_Loaded(object sender, System.Windows.RoutedEventArgs e)
    {
        if (DataContext is FocusSessionViewModel vm)
            await vm.LoadDataCommand.ExecuteAsync(null);
    }

    private void OnIntentionDialogClosing(object sender, DialogClosingEventArgs e)
    {
        if (DataContext is FocusSessionViewModel vm)
            vm.IsIntentionDialogOpen = false;
    }
}
