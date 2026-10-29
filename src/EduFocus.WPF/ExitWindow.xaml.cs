using System.ComponentModel;
using System.Windows;
using System.Windows.Input;
using EduFocus.WPF.ViewModels;

namespace EduFocus.WPF;

public partial class ExitWindow : Window
{
    private readonly ExitViewModel _viewModel;

    public ExitWindow(ExitViewModel viewModel)
    {
        InitializeComponent();

        _viewModel = viewModel;
        DataContext = viewModel;

        _viewModel.PasswordBoxesShouldClear += OnPasswordBoxesShouldClear;
        _viewModel.ExitApproved += OnExitApproved;
        _viewModel.CloseRequested += OnCloseRequested;

        Loaded += OnLoaded;
        Closing += OnClosing;
        Closed += OnClosed;
    }

    private async void OnLoaded(object sender, RoutedEventArgs e)
    {
        await _viewModel.LoadAsync();
        PinBox.Focus();
    }

    private void OnClosing(object? sender, CancelEventArgs e)
    {
        // The clean exit is already underway; closing now would hide partial state.
        if (_viewModel.IsBusy)
            e.Cancel = true;
    }

    private void OnClosed(object? sender, EventArgs e)
    {
        _viewModel.PasswordBoxesShouldClear -= OnPasswordBoxesShouldClear;
        _viewModel.ExitApproved -= OnExitApproved;
        _viewModel.CloseRequested -= OnCloseRequested;
    }

    private void OnPasswordBoxesShouldClear(object? sender, EventArgs e) => PinBox.Clear();

    private void OnExitApproved(object? sender, EventArgs e) => DialogResult = true;

    private void OnCloseRequested(object? sender, EventArgs e) => Close();

    private void PinBox_OnPasswordChanged(object sender, RoutedEventArgs e) =>
        _viewModel.Pin = PinBox.Password;

    private void PinBox_OnKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter)
            return;

        e.Handled = true;

        if (_viewModel.ExitCommand.CanExecute(null))
            _viewModel.ExitCommand.Execute(null);
    }
}
