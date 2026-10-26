using System.Windows.Controls;
using EduFocus.WPF.ViewModels;
using Microsoft.Extensions.DependencyInjection;

namespace EduFocus.WPF.Views;

public partial class AnalyticsView : UserControl
{
    public AnalyticsView()
    {
        InitializeComponent();
        DataContext = App.Services.GetRequiredService<AnalyticsViewModel>();
    }

    private async void UserControl_Loaded(object sender, System.Windows.RoutedEventArgs e)
    {
        if (DataContext is AnalyticsViewModel vm)
            await vm.LoadDataCommand.ExecuteAsync(null);
    }
}
