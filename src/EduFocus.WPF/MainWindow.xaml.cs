using System.Windows;
using EduFocus.WPF.ViewModels;

namespace EduFocus.WPF;

public partial class MainWindow : Window
{
    private readonly MainWindowViewModel _viewModel;
    private readonly Func<ParentModeWindow> _parentModeWindowFactory;

    public MainWindow(
        MainWindowViewModel viewModel,
        Func<ParentModeWindow> parentModeWindowFactory)
    {
        InitializeComponent();
        DataContext = viewModel;
        _viewModel = viewModel;
        _parentModeWindowFactory = parentModeWindowFactory;
    }

    private void ParentModeButton_Click(object sender, RoutedEventArgs e)
    {
        var window = _parentModeWindowFactory();
        window.Owner = this;
        window.ShowDialog();

        // Protection may have been toggled inside Parent Mode.
        _ = _viewModel.Protection.RefreshAsync();
    }
}
