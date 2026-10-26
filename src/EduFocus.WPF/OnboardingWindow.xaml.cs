using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using EduFocus.Infrastructure.Attention.Camera;
using EduFocus.Infrastructure.Persistence;
using EduFocus.Ports.Outbound.Services;

namespace EduFocus.WPF;

public partial class OnboardingWindow : Window
{
    private readonly OnboardingService _onboarding;
    private readonly ParentCredentialService _credentials;
    private readonly IAppLogger _logger;
    private OnboardingProgress? _progress;
    private int? _verifiedCameraIndex;
    private bool _finished;
    private bool _cancelConfirmed;
    private bool _finishing;

    public OnboardingWindow(OnboardingService onboarding, ParentCredentialService credentials, IAppLogger logger)
    {
        InitializeComponent();
        _onboarding = onboarding ?? throw new ArgumentNullException(nameof(onboarding));
        _credentials = credentials ?? throw new ArgumentNullException(nameof(credentials));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        CameraCombo.ItemsSource = Enumerable.Range(0, 4).Select(index => new CameraChoice(index, $"Camera {index + 1} (device {index})")).ToArray();
        CameraCombo.DisplayMemberPath = nameof(CameraChoice.Label);
        CameraCombo.SelectedValuePath = nameof(CameraChoice.Index);
        CameraCombo.SelectedValue = 0;
        CameraCombo.SelectionChanged += (_, _) => _verifiedCameraIndex = null;
        PrivacyAcceptedCheck.Checked += (_, _) => AcceptPrivacyButton.IsEnabled = true;
        PrivacyAcceptedCheck.Unchecked += (_, _) => AcceptPrivacyButton.IsEnabled = false;
        RecoverySavedCheck.Checked += (_, _) => ContinueRecoveryButton.IsEnabled = true;
        RecoverySavedCheck.Unchecked += (_, _) => ContinueRecoveryButton.IsEnabled = false;
        MonitoringCheck.Checked += (_, _) => UpdateCameraRequirements();
        MonitoringCheck.Unchecked += (_, _) => UpdateCameraRequirements();
    }

    private async void OnLoaded(object sender, RoutedEventArgs e)
    {
        _finishing = true;
        try
        {
            _progress = await _onboarding.GetProgressAsync();
            if (_progress.Completed)
            {
                _finished = true;
                DialogResult = true;
                return;
            }
            if (!_progress.PrivacyAccepted) ShowStep(WelcomeStep);
            else if (!_progress.CredentialsConfigured) ShowStep(CreatePinStep);
            else if (_progress.PreferencesSaved) ShowPreferences();
            else ShowStep(ResumePinStep);
        }
        catch (Exception ex)
        {
            _logger.Error("Onboarding state could not be read.", ex);
            PrivacyStatus.Text = "Setup cannot continue because saved state could not be read. Your data was retained.";
            ShowStep(PrivacyStep);
            AcceptPrivacyButton.IsEnabled = false;
        }
        finally { _finishing = false; }
    }

    private void ContinueWelcome(object sender, RoutedEventArgs e)
    {
        if (_progress?.PrivacyAccepted == true) ShowNextCredentialStep();
        else ShowStep(PrivacyStep);
    }

    private async void AcceptPrivacy(object sender, RoutedEventArgs e)
    {
        if (PrivacyAcceptedCheck.IsChecked != true) return;
        _finishing = true;
        AcceptPrivacyButton.IsEnabled = false;
        try
        {
            await _onboarding.AcceptPrivacyNoticeAsync(DateTimeOffset.UtcNow);
            _progress = await _onboarding.GetProgressAsync();
            ShowNextCredentialStep();
        }
        catch (Exception ex)
        {
            _logger.Error("Privacy acceptance could not be saved.", ex);
            PrivacyStatus.Text = "Your acceptance could not be saved. Check data access and try again.";
            AcceptPrivacyButton.IsEnabled = true;
        }
        finally { _finishing = false; }
    }

    private async void CreatePin(object sender, RoutedEventArgs e)
    {
        var pin = CreatePinInput.Password;
        var confirmation = ConfirmPinInput.Password;
        CreatePinInput.Clear();
        ConfirmPinInput.Clear();
        if (!string.Equals(pin, confirmation, StringComparison.Ordinal))
        {
            CreatePinStatus.Text = "The PIN entries do not match.";
            return;
        }
        _finishing = true;
        CreatePinButton.IsEnabled = false;
        try
        {
            using (_logger.BeginCorrelationScope())
            {
                var result = await _credentials.SetUpAsync(pin, DateTimeOffset.UtcNow);
                _progress = await _onboarding.GetProgressAsync();
                ShowRecoveryCode(result.RecoveryCode!);
            }
        }
        catch (ArgumentException)
        {
            CreatePinStatus.Text = "Choose a non-common PIN with 6–8 digits.";
        }
        catch (Exception ex)
        {
            _logger.Error("Parent PIN setup could not be saved.", ex);
            CreatePinStatus.Text = "PIN setup could not be saved. Check data access and try again.";
        }
        finally { _finishing = false; CreatePinButton.IsEnabled = true; }
    }

    private async void ResumePin(object sender, RoutedEventArgs e)
    {
        var pin = ResumePinInput.Password;
        ResumePinInput.Clear();
        _finishing = true;
        ResumePinButton.IsEnabled = false;
        try
        {
            using (_logger.BeginCorrelationScope())
            {
                var result = await _credentials.RegenerateRecoveryCodeAsync(pin, DateTimeOffset.UtcNow);
                if (result.Status == CredentialOperationStatus.Succeeded)
                {
                    ShowRecoveryCode(result.RecoveryCode!);
                    return;
                }
                var state = await _credentials.GetLockoutStatusAsync(DateTimeOffset.UtcNow);
                ResumePinStatus.Text = state.IsLockedOut
                    ? $"Credential entry is locked. Try again in {FormatDuration(state.Remaining)}."
                    : $"PIN not accepted. {Math.Max(0, 5 - state.FailedAttempts)} attempt(s) remain.";
            }
        }
        catch (Exception ex)
        {
            _logger.Error("Onboarding could not verify the saved PIN.", ex);
            ResumePinStatus.Text = "PIN verification is unavailable. Saved setup was retained.";
        }
        finally { _finishing = false; ResumePinButton.IsEnabled = true; }
    }

    private void ShowRecoveryCode(string code)
    {
        RecoveryCodeText.Text = code;
        RecoverySavedCheck.IsChecked = false;
        ContinueRecoveryButton.IsEnabled = false;
        CopyStatus.Text = string.Empty;
        ShowStep(RecoveryCodeStep);
    }

    private void CopyRecoveryCode(object sender, RoutedEventArgs e)
    {
        try
        {
            Clipboard.SetText(RecoveryCodeText.Text);
            CopyStatus.Text = "Copied. Store it somewhere private; clear your clipboard after saving.";
        }
        catch
        {
            CopyStatus.Text = "Copy was unavailable. You can select and copy the code manually.";
        }
    }

    private void ContinueRecovery(object sender, RoutedEventArgs e)
    {
        if (RecoverySavedCheck.IsChecked != true) return;
        RecoveryCodeText.Clear();
        ShowPreferences();
    }

    private async void TestCamera(object sender, RoutedEventArgs e)
    {
        if (CameraCombo.SelectedValue is not int cameraIndex) return;
        _finishing = true;
        TestCameraButton.IsEnabled = false;
        CameraStatus.Text = "Checking access… No image will be saved.";
        try
        {
            var result = await Task.Run(() => CameraAccessProbe.Check(cameraIndex));
            _verifiedCameraIndex = result.Accessible ? cameraIndex : null;
            CameraStatus.Text = result.Accessible
                ? "Camera access is available. The device was opened and closed without saving a frame."
                : "Camera access was denied or the device is unavailable. Choose another device, or leave monitoring off.";
        }
        catch (Exception ex)
        {
            _logger.Error("Camera access check failed.", ex);
            _verifiedCameraIndex = null;
            CameraStatus.Text = "Camera access could not be checked. You can continue with monitoring off.";
        }
        finally { _finishing = false; TestCameraButton.IsEnabled = true; UpdateCameraRequirements(); }
    }

    private async void FinishSetup(object sender, RoutedEventArgs e)
    {
        var monitoring = MonitoringCheck.IsChecked == true;
        var cameraIndex = CameraCombo.SelectedValue is int index ? index : (int?)null;
        if (monitoring && (_verifiedCameraIndex is null || _verifiedCameraIndex != cameraIndex))
        {
            PreferencesStatus.Text = "Check access for the selected camera before enabling monitoring.";
            return;
        }

        FinishSetupButton.IsEnabled = false;
        _finishing = true;
        try
        {
            using (_logger.BeginCorrelationScope())
            {
                await _onboarding.SavePreferencesAsync(monitoring, cameraIndex, AutoStartCheck.IsChecked == true, DateTimeOffset.UtcNow);
                var startupRegistered = await Task.Run(() => WindowsStartupRegistration.SetEnabled(AutoStartCheck.IsChecked == true));
                if (!startupRegistered)
                {
                    PreferencesStatus.Text = "Windows could not register auto-start. Install EduFocus in Program Files or turn the option off.";
                    return;
                }
                await _onboarding.CompleteAsync(DateTimeOffset.UtcNow);
            }
            _finishing = false;
            _finished = true;
            DialogResult = true;
        }
        catch (Exception ex)
        {
            _logger.Error("Onboarding preferences could not be completed.", ex);
            PreferencesStatus.Text = "Setup could not be completed. Your saved steps were kept; try again.";
        }
        finally { _finishing = false; FinishSetupButton.IsEnabled = true; }
    }

    private void ShowPreferences()
    {
        if (_progress is not null)
        {
            MonitoringCheck.IsChecked = _progress.MonitoringEnabled;
            AutoStartCheck.IsChecked = _progress.StartWithWindows;
            if (_progress.CameraIndex is { } index) CameraCombo.SelectedValue = index;
        }
        _verifiedCameraIndex = null;
        CameraStatus.Text = _progress?.MonitoringEnabled == true
            ? "Check camera access again before finishing setup."
            : "Monitoring is off until you opt in.";
        UpdateCameraRequirements();
        ShowStep(PreferencesStep);
    }

    private void UpdateCameraRequirements()
    {
        var enabled = MonitoringCheck.IsChecked == true;
        CameraCombo.IsEnabled = true;
        TestCameraButton.IsEnabled = true;
        CameraStatus.TextWrapping = TextWrapping.Wrap;
        if (!enabled && _verifiedCameraIndex is null) CameraStatus.Text = "Monitoring is off. Camera access is optional.";
    }

    private void ShowNextCredentialStep()
    {
        if (_progress?.CredentialsConfigured == true)
        {
            if (_progress.PreferencesSaved) ShowPreferences();
            else ShowStep(ResumePinStep);
        }
        else ShowStep(CreatePinStep);
    }

    private void ResumeSetup(object sender, RoutedEventArgs e)
    {
        if (_progress is null) return;
        if (!_progress.PrivacyAccepted) ShowStep(WelcomeStep);
        else ShowNextCredentialStep();
    }

    private async void CancelSetup(object sender, RoutedEventArgs e)
    {
        if (_finishing) return;
        if (MessageBox.Show("Setup is incomplete. EduFocus will remain locked and resume here next time. Exit now?", "Cancel setup", MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes) return;
        _finishing = true;
        await Task.Run(() => WindowsStartupRegistration.SetEnabled(false));
        _finishing = false;
        _cancelConfirmed = true;
        DialogResult = false;
    }

    private async void OnClosing(object? sender, System.ComponentModel.CancelEventArgs e)
    {
        if (_finished || _cancelConfirmed) return;
        if (_finishing) { e.Cancel = true; return; }
        if (MessageBox.Show("Setup is incomplete. Completed steps are saved, and setup will resume next time. Exit now?", "Setup incomplete", MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes)
            e.Cancel = true;
        else
        {
            e.Cancel = true;
            _finishing = true;
            await Task.Run(() => WindowsStartupRegistration.SetEnabled(false));
            _finishing = false;
            _cancelConfirmed = true;
            Close();
        }
    }

    private void ShowStep(StackPanel step)
    {
        foreach (var panel in new[] { WelcomeStep, PrivacyStep, CreatePinStep, ResumePinStep, RecoveryCodeStep, PreferencesStep, PausedStep })
            panel.Visibility = ReferenceEquals(panel, step) ? Visibility.Visible : Visibility.Collapsed;
    }

    private static string FormatDuration(TimeSpan duration)
    {
        var rounded = TimeSpan.FromSeconds(Math.Ceiling(Math.Max(0, duration.TotalSeconds)));
        return $"{(int)rounded.TotalMinutes:00}:{rounded.Seconds:00}";
    }

    private sealed record CameraChoice(int Index, string Label);
}

internal static class WindowsStartupRegistration
{
    private const string TaskName = "EduFocus AutoStart";

    public static bool SetEnabled(bool enabled)
    {
        try
        {
            if (enabled)
            {
                var executable = Environment.ProcessPath;
                if (string.IsNullOrWhiteSpace(executable)) return false;
                executable = Path.GetFullPath(executable);
                if (!IsProtectedInstallPath(executable)) return false;
                var schtasks = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "schtasks.exe");
                return Run(schtasks, "/Create", "/TN", TaskName, "/SC", "ONLOGON", "/TR", $"\"{executable}\"", "/RL", "HIGHEST", "/F") == 0;
            }
            var systemSchtasks = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "schtasks.exe");
            if (Run(systemSchtasks, "/Query", "/TN", TaskName) != 0) return true;
            return Run(systemSchtasks, "/Delete", "/TN", TaskName, "/F") == 0;
        }
        catch { return false; }
    }

    private static bool IsProtectedInstallPath(string executable)
    {
        var roots = new[]
        {
            Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
            Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86)
        }.Where(path => !string.IsNullOrWhiteSpace(path));
        return roots.Any(root => executable.StartsWith(Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase));
    }

    private static int Run(string executable, params string[] arguments)
    {
        var start = new ProcessStartInfo(executable)
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            WindowStyle = ProcessWindowStyle.Hidden
        };
        foreach (var argument in arguments) start.ArgumentList.Add(argument);
        using var process = Process.Start(start);
        if (process is null) return -1;
        if (!process.WaitForExit(15_000))
        {
            process.Kill(entireProcessTree: true);
            return -1;
        }
        return process.ExitCode;
    }
}
