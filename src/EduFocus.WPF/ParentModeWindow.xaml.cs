using System.Windows;
using System.Windows.Input;
using System.Windows.Threading;
using EduFocus.WPF.ViewModels;

namespace EduFocus.WPF;

public partial class ParentModeWindow : Window
{
    private readonly ParentModeViewModel _viewModel;
    private readonly DispatcherTimer _timer;

    public ParentModeWindow(ParentModeViewModel viewModel)
    {
        InitializeComponent();

        _viewModel = viewModel;
        DataContext = viewModel;

        _viewModel.PasswordBoxesShouldClear += OnPasswordBoxesShouldClear;
        _viewModel.CloseRequested += OnCloseRequested;

        _timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        _timer.Tick += OnTimerTick;

        Loaded += OnLoaded;
        Closed += OnClosed;
    }

    private async void OnLoaded(object sender, RoutedEventArgs e)
    {
        _timer.Start();
        await _viewModel.LoadCommand.ExecuteAsync(null);

        if (_viewModel.IsLoginVisible)
            LoginPinBox.Focus();
    }

    private void OnClosed(object? sender, EventArgs e)
    {
        _timer.Stop();
        _timer.Tick -= OnTimerTick;
        _viewModel.PasswordBoxesShouldClear -= OnPasswordBoxesShouldClear;
        _viewModel.CloseRequested -= OnCloseRequested;
        _viewModel.HandleWindowClosed();
    }

    private void OnTimerTick(object? sender, EventArgs e) => _viewModel.HandleTick();

    private void OnPasswordBoxesShouldClear(object? sender, EventArgs e)
    {
        LoginPinBox.Clear();
        CurrentPinBox.Clear();
        NewPinBox.Clear();
        ConfirmNewPinBox.Clear();
        RecoveryCodeBox.Clear();
        ResetNewPinBox.Clear();
        ConfirmResetPinBox.Clear();
    }

    private void OnCloseRequested(object? sender, EventArgs e) => Close();

    private void LoginPinBox_OnPasswordChanged(object sender, RoutedEventArgs e) =>
        _viewModel.LoginPin = LoginPinBox.Password;

    private void CurrentPinBox_OnPasswordChanged(object sender, RoutedEventArgs e) =>
        _viewModel.CurrentPin = CurrentPinBox.Password;

    private void NewPinBox_OnPasswordChanged(object sender, RoutedEventArgs e) =>
        _viewModel.NewPin = NewPinBox.Password;

    private void ConfirmNewPinBox_OnPasswordChanged(object sender, RoutedEventArgs e) =>
        _viewModel.ConfirmNewPin = ConfirmNewPinBox.Password;

    private void RecoveryCodeBox_OnPasswordChanged(object sender, RoutedEventArgs e) =>
        _viewModel.RecoveryCode = RecoveryCodeBox.Password;

    private void ResetNewPinBox_OnPasswordChanged(object sender, RoutedEventArgs e) =>
        _viewModel.ResetNewPin = ResetNewPinBox.Password;

    private void ConfirmResetPinBox_OnPasswordChanged(object sender, RoutedEventArgs e) =>
        _viewModel.ConfirmResetPin = ConfirmResetPinBox.Password;

    private void LoginPinBox_OnKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter)
            return;

        e.Handled = true;

        if (_viewModel.LoginCommand.CanExecute(null))
            _viewModel.LoginCommand.Execute(null);
    }
}
