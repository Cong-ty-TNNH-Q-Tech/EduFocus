using System.ComponentModel;
using System.Windows;
using System.Windows.Threading;
using EduFocus.WPF.Services;
using EduFocus.WPF.ViewModels;

namespace EduFocus.WPF;

public partial class MainWindow : Window
{
    private readonly MainWindowViewModel _viewModel;
    private readonly Func<ParentModeWindow> _parentModeWindowFactory;
    private readonly TrayIconService _tray;
    private readonly AppExitState _exitState;
    private readonly DispatcherTimer _refreshTimer;

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

        // UC10: protection/session/camera refresh near realtime while the
        // student overview is visible; hidden windows stop polling.
        _refreshTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(15) };
        _refreshTimer.Tick += OnRefreshTick;
        Loaded += OnLoaded;
        IsVisibleChanged += OnIsVisibleChanged;
        Closed += OnClosed;
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

    private async void OnLoaded(object sender, RoutedEventArgs e)
    {
        await RefreshDashboardAsync();

        if (IsVisible)
            _refreshTimer.Start();
    }

    private void OnIsVisibleChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        if (!IsVisible)
        {
            _refreshTimer.Stop();
            return;
        }

        _ = RefreshDashboardAsync();
        _refreshTimer.Start();
    }

    private async void OnRefreshTick(object? sender, EventArgs e) =>
        await RefreshDashboardAsync();

    private void OnClosed(object? sender, EventArgs e)
    {
        _refreshTimer.Stop();
        _refreshTimer.Tick -= OnRefreshTick;
    }

    private async Task RefreshDashboardAsync()
    {
        await _viewModel.Protection.RefreshAsync();
        await _viewModel.Dashboard.RefreshAsync();
    }

    private void ParentModeButton_Click(object sender, RoutedEventArgs e)
    {
        var window = _parentModeWindowFactory();
        window.Owner = this;
        window.ShowDialog();

        // Protection and the today dashboard may have changed inside Parent Mode.
        _ = RefreshDashboardAsync();
    }
}
