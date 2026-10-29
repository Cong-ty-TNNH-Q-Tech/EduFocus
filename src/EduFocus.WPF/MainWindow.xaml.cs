using System.ComponentModel;
using System.Windows;
using EduFocus.WPF.Services;
using EduFocus.WPF.ViewModels;

namespace EduFocus.WPF;

public partial class MainWindow : Window
{
    private readonly MainWindowViewModel _viewModel;
    private readonly Func<ParentModeWindow> _parentModeWindowFactory;
    private readonly TrayIconService _tray;
    private readonly AppExitState _exitState;

    public MainWindow(
        MainWindowViewModel viewModel,
        Func<ParentModeWindow> parentModeWindowFactory,
        TrayIconService tray,
        AppExitState exitState)
    {
        InitializeComponent();
        DataContext = viewModel;
        _viewModel = viewModel;
        _parentModeWindowFactory = parentModeWindowFactory;
        _tray = tray;
        _exitState = exitState;
    }

    protected override void OnClosing(CancelEventArgs e)
    {
        // UC12: X and Alt+F4 only hide the window; quitting needs the PIN exit flow.
        if (!_exitState.IsExitApproved)
        {
            e.Cancel = true;
            Hide();
            _tray.NotifyHiddenToTray(_viewModel.Protection.IsEnabled);
        }

        base.OnClosing(e);
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
