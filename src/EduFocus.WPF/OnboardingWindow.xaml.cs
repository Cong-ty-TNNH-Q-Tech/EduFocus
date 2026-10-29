using System.ComponentModel;
using System.Windows;
using System.Windows.Input;
using System.Windows.Threading;
using EduFocus.WPF.ViewModels;

namespace EduFocus.WPF;

public partial class OnboardingWindow : Window
{
    private readonly OnboardingViewModel _viewModel;

    public OnboardingWindow(OnboardingViewModel viewModel)
    {
        InitializeComponent();

        _viewModel = viewModel;
        DataContext = viewModel;

        _viewModel.PasswordBoxesShouldClear += OnPasswordBoxesShouldClear;
        _viewModel.CloseRequested += OnCloseRequested;
        _viewModel.Completed += OnCompleted;
        _viewModel.PropertyChanged += ViewModel_PropertyChanged;

        Loaded += OnLoaded;
        Closed += OnClosed;
    }

    private async void OnLoaded(object sender, RoutedEventArgs e)
    {
        await _viewModel.LoadCommand.ExecuteAsync(null);

        if (IsLoaded)
            FocusCurrentStep();
    }

    private void OnClosed(object? sender, EventArgs e)
    {
        _viewModel.PasswordBoxesShouldClear -= OnPasswordBoxesShouldClear;
        _viewModel.CloseRequested -= OnCloseRequested;
        _viewModel.Completed -= OnCompleted;
        _viewModel.PropertyChanged -= ViewModel_PropertyChanged;
        _viewModel.HandleWindowClosed();
    }

    private void OnCompleted(object? sender, EventArgs e) => DialogResult = true;

    private void OnCloseRequested(object? sender, EventArgs e) => Close();

    private void OnPasswordBoxesShouldClear(object? sender, EventArgs e)
    {
        PinBox.Clear();
        ConfirmPinBox.Clear();
        ResumePinBox.Clear();
    }

    private void PinBox_OnPasswordChanged(object sender, RoutedEventArgs e) =>
        _viewModel.Pin = PinBox.Password;

    private void ConfirmPinBox_OnPasswordChanged(object sender, RoutedEventArgs e) =>
        _viewModel.ConfirmPin = ConfirmPinBox.Password;

    private void ResumePinBox_OnPasswordChanged(object sender, RoutedEventArgs e) =>
        _viewModel.ResumePin = ResumePinBox.Password;

    private void PinBox_OnKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter)
            return;

        e.Handled = true;

        if (_viewModel.ConfigurePinCommand.CanExecute(null))
            _viewModel.ConfigurePinCommand.Execute(null);
    }

    private void ResumePinBox_OnKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter)
            return;

        e.Handled = true;

        if (_viewModel.ResumeWithPinCommand.CanExecute(null))
            _viewModel.ResumeWithPinCommand.Execute(null);
    }

    private void FocusCurrentStep()
    {
        switch (_viewModel.View)
        {
            case OnboardingStep.Error:
                RetryButton.Focus();
                break;
            case OnboardingStep.Welcome:
                WelcomeContinueButton.Focus();
                break;
            case OnboardingStep.Privacy:
                PrivacyCheckBox.Focus();
                break;
            case OnboardingStep.Preferences:
                MonitoringCheckBox.Focus();
                break;
            case OnboardingStep.Pin:
                PinBox.Focus();
                break;
            case OnboardingStep.ResumePin:
                ResumePinBox.Focus();
                break;
            case OnboardingStep.RecoveryCode:
                RecoveryCodeBox.Focus();
                RecoveryCodeBox.SelectAll();
                break;
        }
    }

    private void ViewModel_PropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName != nameof(OnboardingViewModel.View) || !IsLoaded)
            return;

        Dispatcher.BeginInvoke(FocusCurrentStep, DispatcherPriority.Input);
    }
}
