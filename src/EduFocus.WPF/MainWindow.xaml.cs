using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;
using EduFocus.Domain.Entities;
using EduFocus.Domain.Enums;
using EduFocus.Infrastructure.Persistence;
using EduFocus.Ports.Outbound.Services;

namespace EduFocus.WPF;

public partial class MainWindow : Window
{
    private enum Page { Student, Login, Recovery, NewRecovery, Parent, ChangePin }

    private readonly ParentCredentialService _credentials;
    private readonly ProtectionCoordinator _protection;
    private readonly IAppLogger _logger;
    private readonly ParentModeSession _parentSession = new();
    private readonly DispatcherTimer _timer;
    private readonly bool _credentialsConfigured;
    private DateTimeOffset? _lockoutUntilUtc;
    private bool _authenticationUnavailable;
    private Page _page = Page.Student;

    public MainWindow(ParentCredentialService credentials, ProtectionCoordinator protection, IAppLogger logger, bool credentialsConfigured)
    {
        InitializeComponent();
        _credentials = credentials ?? throw new ArgumentNullException(nameof(credentials));
        _protection = protection ?? throw new ArgumentNullException(nameof(protection));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _credentialsConfigured = credentialsConfigured;
        if (!credentialsConfigured)
        {
            OpenLoginButton.IsEnabled = false;
            StudentStatus.Text = "Parent PIN setup is required before protected settings can be used. Complete first-run setup to continue.";
        }
        _timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        _timer.Tick += OnTimerTick;
        _timer.Start();
    }

    private async void OnLoaded(object sender, RoutedEventArgs e)
    {
        if (_credentialsConfigured) await RefreshLockoutStatus();
    }

    private async void OpenLogin(object sender, RoutedEventArgs e)
    {
        if (!_credentialsConfigured) return;
        ClearSecrets();
        SetPage(Page.Login);
        await RefreshLockoutStatus();
        PinInput.Focus();
    }

    private void BackToStudent(object sender, RoutedEventArgs e)
    {
        ClearSecrets();
        SetPage(Page.Student);
    }

    private async void Login(object sender, RoutedEventArgs e)
    {
        var pin = PinInput.Password;
        PinInput.Clear();
        LoginButton.IsEnabled = false;
        try
        {
            using (_logger.BeginCorrelationScope())
            {
                var result = await _credentials.AuthenticatePinAsync(pin, DateTimeOffset.UtcNow);
                if (result.Status == CredentialOperationStatus.Succeeded)
                {
                    _parentSession.Unlock(DateTimeOffset.UtcNow);
                    SetPage(Page.Parent);
                    UpdateSessionCountdown();
                    await RefreshProtectionStatus();
                    return;
                }
                await RefreshLockoutStatus("The PIN was not accepted.");
            }
        }
        catch (Exception ex)
        {
            _logger.Error("Parent authentication could not be completed.", ex);
            LoginStatus.Text = "Authentication is unavailable. Please try again.";
        }
        finally { LoginButton.IsEnabled = _credentialsConfigured && !_IsLockedOut(); }
    }

    private void OpenRecovery(object sender, RoutedEventArgs e)
    {
        if (!_credentialsConfigured) return;
        ClearSecrets();
        SetPage(Page.Recovery);
        RecoveryStatus.Text = LoginStatus.Text;
        RecoverButton.IsEnabled = !_IsLockedOut();
        RecoveryInput.Focus();
    }

    private async void RecoverPin(object sender, RoutedEventArgs e)
    {
        var code = RecoveryInput.Password;
        var newPin = RecoveryNewPinInput.Password;
        RecoveryInput.Clear();
        RecoveryNewPinInput.Clear();
        RecoverButton.IsEnabled = false;
        try
        {
            using (_logger.BeginCorrelationScope())
            {
                var result = await _credentials.RecoverPinAsync(code, newPin, DateTimeOffset.UtcNow);
                if (result.Status == CredentialOperationStatus.Succeeded)
                {
                    _parentSession.Lock();
                    NewRecoveryCodeText.Text = result.RecoveryCode;
                    LoginStatus.Text = "PIN updated. Sign in again with the new PIN.";
                    SetPage(Page.NewRecovery);
                    return;
                }
                await RefreshLockoutStatus("The recovery code was not accepted.");
                SetPage(Page.Recovery);
            }
        }
        catch (Exception ex)
        {
            _logger.Error("PIN recovery could not be completed.", ex);
            RecoveryStatus.Text = "Recovery is unavailable. Check the code and try again.";
        }
        finally { RecoverButton.IsEnabled = !_IsLockedOut(); }
    }

    private void FinishRecovery(object sender, RoutedEventArgs e)
    {
        NewRecoveryCodeText.Text = string.Empty;
        ClearSecrets();
        SetPage(Page.Login);
        PinInput.Focus();
    }

    private void OpenChangePin(object sender, RoutedEventArgs e)
    {
        if (!EnsureParentSession()) return;
        CurrentPinInput.Clear();
        NewPinInput.Clear();
        ChangePinStatus.Text = string.Empty;
        SetPage(Page.ChangePin);
        CurrentPinInput.Focus();
    }

    private async void ChangePin(object sender, RoutedEventArgs e)
    {
        if (!EnsureParentSession()) return;
        var current = CurrentPinInput.Password;
        var next = NewPinInput.Password;
        CurrentPinInput.Clear();
        NewPinInput.Clear();
        try
        {
            using (_logger.BeginCorrelationScope())
            {
                var result = await _credentials.ChangePinAsync(current, next, DateTimeOffset.UtcNow);
                if (result.Status == CredentialOperationStatus.Succeeded)
                {
                    _parentSession.Lock();
                    SetPage(Page.Login);
                    LoginStatus.Text = "PIN changed. Sign in again to enter Parent Mode.";
                    return;
                }
                ChangePinStatus.Text = result.Status == CredentialOperationStatus.LockedOut
                    ? "Credential entry is temporarily locked. Return to login to see the remaining time."
                    : "The current PIN was not accepted.";
            }
        }
        catch (Exception ex)
        {
            _logger.Error("PIN change could not be completed.", ex);
            ChangePinStatus.Text = "PIN change is unavailable. Check the new PIN and try again.";
        }
    }

    private void ShowParent(object sender, RoutedEventArgs e)
    {
        if (EnsureParentSession())
        {
            SetPage(Page.Parent);
            _ = RefreshProtectionStatus();
        }
    }

    private async void ToggleProtection(object sender, RoutedEventArgs e)
    {
        if (!EnsureParentSession()) return;
        ToggleProtectionButton.IsEnabled = false;
        try
        {
            var current = await _protection.GetStatusAsync();
            if (!current.EngineAvailable)
            {
                ProtectionStatusText.Text = current.State == ProtectionState.On
                    ? "Protection is saved as ON, but the engine is unavailable; its resources cannot be changed safely."
                    : "The protection engine is not installed. Protection remains OFF.";
                return;
            }
            await _protection.SetProtectionAsync(current.State != ProtectionState.On, _parentSession, DateTimeOffset.UtcNow);
            await RefreshProtectionStatus();
        }
        catch (Exception ex)
        {
            _logger.Error("Protection state could not be changed.", ex);
            ProtectionStatusText.Text = "Protection could not be changed. The saved state and resources were retained.";
            await RefreshProtectionStatus();
        }
        finally { ToggleProtectionButton.IsEnabled = _protectionEngineAvailable; }
    }

    private bool _protectionEngineAvailable;

    private async Task RefreshProtectionStatus()
    {
        try
        {
            var status = await _protection.GetStatusAsync();
            _protectionEngineAvailable = status.EngineAvailable;
            ProtectionStatusText.Text = !status.EngineAvailable
                ? status.State == ProtectionState.On
                    ? "Protection is saved as ON, but the engine is unavailable; its resources cannot be changed safely."
                    : "The protection engine is not installed. Protection remains OFF."
                : status.State == ProtectionState.On ? "Protection is ON." : "Protection is OFF.";
            ToggleProtectionButton.Content = status.State == ProtectionState.On ? "Turn Protection off" : "Turn Protection on";
            ToggleProtectionButton.IsEnabled = status.EngineAvailable;
        }
        catch (Exception ex)
        {
            _protectionEngineAvailable = false;
            _logger.Error("Protection state could not be read.", ex);
            ProtectionStatusText.Text = "Protection state could not be read. Controls remain locked.";
            ToggleProtectionButton.IsEnabled = false;
        }
    }

    private void LockNow(object sender, RoutedEventArgs e)
    {
        _parentSession.Lock();
        ClearSecrets();
        SetPage(Page.Login);
        LoginStatus.Text = "Parent Mode is locked.";
    }

    private void OnUserActivity(object sender, RoutedEventArgs e)
    {
        if (!_parentSession.RecordActivity(DateTimeOffset.UtcNow) && _page is Page.Parent or Page.ChangePin)
            ExpireParentSession();
        else if (_page == Page.Parent)
            UpdateSessionCountdown();
    }

    private void OnTimerTick(object? sender, EventArgs e)
    {
        if (_page is Page.Parent or Page.ChangePin)
        {
            if (!_parentSession.IsActive(DateTimeOffset.UtcNow)) ExpireParentSession();
            else if (_page == Page.Parent) UpdateSessionCountdown();
        }
        UpdateLockoutCountdown();
    }

    private bool EnsureParentSession()
    {
        if (_parentSession.IsActive(DateTimeOffset.UtcNow)) return true;
        ExpireParentSession();
        return false;
    }

    private void ExpireParentSession()
    {
        _parentSession.Lock();
        ClearSecrets();
        SetPage(Page.Login);
        LoginStatus.Text = "Parent Mode was locked after five minutes without activity.";
        _ = RefreshLockoutStatus();
    }

    private async Task RefreshLockoutStatus(string? failureMessage = null)
    {
        if (!_credentialsConfigured) return;
        try
        {
            var status = await _credentials.GetLockoutStatusAsync(DateTimeOffset.UtcNow);
            _authenticationUnavailable = false;
            _lockoutUntilUtc = status.IsLockedOut ? status.LockoutUntilUtc : null;
            var attemptsLeft = Math.Max(0, 5 - status.FailedAttempts);
            var message = failureMessage is null ? string.Empty : $"{failureMessage} ";
            if (status.IsLockedOut)
                message += $"Try again in {FormatDuration(status.Remaining)}. PIN and recovery share this lockout.";
            else if (failureMessage is not null)
                message += $"{attemptsLeft} attempt(s) remain before a 15-minute lockout.";
            LoginStatus.Text = message;
            RecoveryStatus.Text = message;
            LoginButton.IsEnabled = !status.IsLockedOut;
            RecoverButton.IsEnabled = !status.IsLockedOut;
        }
        catch (Exception ex)
        {
            _authenticationUnavailable = true;
            _lockoutUntilUtc = null;
            _logger.Error("Parent credential lockout state could not be read.", ex);
            LoginStatus.Text = "Authentication state is unavailable. Protected settings remain locked.";
            RecoveryStatus.Text = LoginStatus.Text;
            LoginButton.IsEnabled = false;
            RecoverButton.IsEnabled = false;
        }
    }

    private void UpdateLockoutCountdown()
    {
        if (_lockoutUntilUtc is not { } until) return;
        var remaining = until - DateTimeOffset.UtcNow;
        if (remaining <= TimeSpan.Zero)
        {
            _lockoutUntilUtc = null;
            LoginButton.IsEnabled = _credentialsConfigured && !_authenticationUnavailable;
            RecoverButton.IsEnabled = _credentialsConfigured && !_authenticationUnavailable;
            LoginStatus.Text = string.Empty;
            RecoveryStatus.Text = string.Empty;
            return;
        }
        var message = $"Try again in {FormatDuration(remaining)}. PIN and recovery share this lockout.";
        LoginStatus.Text = message;
        RecoveryStatus.Text = message;
    }

    private bool _IsLockedOut() => _authenticationUnavailable || (_lockoutUntilUtc is { } until && until > DateTimeOffset.UtcNow);

    private void UpdateSessionCountdown()
    {
        var remaining = _parentSession.Remaining(DateTimeOffset.UtcNow);
        SessionCountdown.Text = $"Locks after inactivity: {FormatDuration(remaining)}";
    }

    private static string FormatDuration(TimeSpan duration)
    {
        var rounded = TimeSpan.FromSeconds(Math.Ceiling(Math.Max(0, duration.TotalSeconds)));
        return $"{(int)rounded.TotalMinutes:00}:{rounded.Seconds:00}";
    }

    private void ClearSecrets()
    {
        PinInput.Clear();
        RecoveryInput.Clear();
        RecoveryNewPinInput.Clear();
        CurrentPinInput.Clear();
        NewPinInput.Clear();
    }

    private void SetPage(Page page)
    {
        _page = page;
        StudentPanel.Visibility = page == Page.Student ? Visibility.Visible : Visibility.Collapsed;
        LoginPanel.Visibility = page == Page.Login ? Visibility.Visible : Visibility.Collapsed;
        RecoveryPanel.Visibility = page == Page.Recovery ? Visibility.Visible : Visibility.Collapsed;
        NewRecoveryPanel.Visibility = page == Page.NewRecovery ? Visibility.Visible : Visibility.Collapsed;
        ParentPanel.Visibility = page == Page.Parent ? Visibility.Visible : Visibility.Collapsed;
        ChangePinPanel.Visibility = page == Page.ChangePin ? Visibility.Visible : Visibility.Collapsed;
    }
}
