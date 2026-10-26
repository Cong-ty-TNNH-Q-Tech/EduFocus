using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;
using System.ComponentModel;
using EduFocus.Domain.Entities;
using EduFocus.Domain.Enums;
using EduFocus.Infrastructure.Persistence;
using EduFocus.Ports.Outbound.Services;

namespace EduFocus.WPF;

public partial class MainWindow : Window
{
    private enum Page { Student, Login, Recovery, NewRecovery, Parent, Blocklist, ChangePin }
    private sealed record ScheduleListItem(Guid Id, string Label, int Days, int Start, int End, string? Note);
    private sealed record BlockRuleListItem(Guid Id, string Label, bool Enabled, bool CanChange);

    private readonly ParentCredentialService _credentials;
    private readonly ProtectionCoordinator _protection;
    private readonly ScheduleService _schedule;
    private readonly BlockRuleService _blockRules;
    private readonly IAppLogger _logger;
    private readonly ParentModeSession _parentSession = new();
    private readonly DispatcherTimer _timer;
    private readonly bool _credentialsConfigured;
    private DateTimeOffset? _lockoutUntilUtc;
    private bool _authenticationUnavailable;
    private Page _page = Page.Student;
    private DateTimeOffset _lastScheduleEvaluationUtc = DateTimeOffset.MinValue;
    private bool _allowClose;
    private bool _systemShutdown;
    private bool _exitInProgress;

    public MainWindow(ParentCredentialService credentials, ProtectionCoordinator protection, ScheduleService schedule, BlockRuleService blockRules, IAppLogger logger, bool credentialsConfigured)
    {
        InitializeComponent();
        _credentials = credentials ?? throw new ArgumentNullException(nameof(credentials));
        _protection = protection ?? throw new ArgumentNullException(nameof(protection));
        _schedule = schedule ?? throw new ArgumentNullException(nameof(schedule));
        _blockRules = blockRules ?? throw new ArgumentNullException(nameof(blockRules));
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

    protected override async void OnClosing(CancelEventArgs e)
    {
        if (_allowClose || _systemShutdown)
        {
            base.OnClosing(e);
            return;
        }
        e.Cancel = true;
        base.OnClosing(e);
        if (_exitInProgress || e.Cancel == false) return;

        _exitInProgress = true;
        try
        {
            var prompt = new ExitPinDialog(async pin =>
            {
                using (_logger.BeginCorrelationScope())
                {
                    var result = await _credentials.AuthenticatePinAsync(pin, DateTimeOffset.UtcNow);
                    return result.Status == CredentialOperationStatus.Succeeded;
                }
            }) { Owner = this };
            if (prompt.ShowDialog() != true) return;

            await _protection.CleanExitAsync(DateTimeOffset.UtcNow);
            _parentSession.Lock();
            _allowClose = true;
            Close();
        }
        catch (Exception ex)
        {
            _logger.Error("Authorized application exit could not be completed safely.", ex);
            MessageBox.Show(this, "EduFocus could not safely save the exit state or remove protection resources. The app will remain open.",
                "Exit unavailable", MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally { if (!_allowClose) _exitInProgress = false; }
    }

    public void AllowSystemShutdown() => _systemShutdown = true;

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
                    await RefreshSchedules();
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
            _ = RefreshSchedules();
        }
    }

    private async void OpenBlocklist(object sender, RoutedEventArgs e)
    {
        if (!EnsureParentSession()) return;
        BlockRuleStatus.Text = string.Empty;
        SetPage(Page.Blocklist);
        await RefreshBlockRules();
    }

    private async void ToggleBuiltInRule(object sender, RoutedEventArgs e)
    {
        if (!EnsureParentSession() || BlockRuleList.SelectedItem is not BlockRuleListItem selected) return;
        try
        {
            await _blockRules.SetEnabledAsync(selected.Id, !selected.Enabled, _parentSession, DateTimeOffset.UtcNow);
            BlockRuleStatus.Text = "Built-in rule updated.";
            await RefreshBlockRules();
        }
        catch (Exception ex)
        {
            _logger.Error("Built-in block rule could not be changed.", ex);
            BlockRuleStatus.Text = "Rule was not changed. Protection settings were retained.";
        }
    }

    private void OnBlockRuleSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (BlockRuleList.SelectedItem is not BlockRuleListItem selected)
        {
            ToggleBuiltInRuleButton.IsEnabled = false;
            ToggleBuiltInRuleButton.Content = "Select a rule";
            return;
        }
        ToggleBuiltInRuleButton.IsEnabled = selected.CanChange && _parentSession.IsActive(DateTimeOffset.UtcNow);
        ToggleBuiltInRuleButton.Content = selected.CanChange
            ? selected.Enabled ? "Disable selected rule" : "Enable selected rule"
            : "Essential allowlist is protected";
    }

    private async Task RefreshBlockRules()
    {
        var rules = await _blockRules.GetRulesAsync();
        BlockRuleList.ItemsSource = rules.Select(rule =>
        {
            var canChange = rule.IsBuiltIn && !(rule.Effect == BlockRuleEffect.Allow && DefaultBlockRuleSeeder.IsEssentialAllowlist(rule.NormalizedValue));
            return new BlockRuleListItem(rule.Id,
                $"{rule.NormalizedValue} — {rule.Category} / {rule.Effect} / {(rule.IsEnabled ? "enabled" : "disabled")}{(rule.IsBuiltIn ? " / built-in" : " / parent")}{(canChange ? string.Empty : " / protected")}",
                rule.IsEnabled, canChange);
        }).ToArray();
        ToggleBuiltInRuleButton.IsEnabled = false;
        ToggleBuiltInRuleButton.Content = "Select a rule";
    }

    private async void AddSchedule(object sender, RoutedEventArgs e)
    {
        if (!EnsureParentSession()) return;
        if (!int.TryParse(ScheduleDaysInput.Text, out var days) || !int.TryParse(ScheduleStartInput.Text, out var start) || !int.TryParse(ScheduleEndInput.Text, out var end))
        {
            ScheduleStatusText.Text = "Enter numeric days and minute values for the schedule.";
            return;
        }
        try
        {
            if (ScheduleList.SelectedItem is ScheduleListItem selected)
                await _schedule.UpdateAsync(selected.Id, days, start, end, ScheduleLabelInput.Text, true, _parentSession, DateTimeOffset.UtcNow);
            else
                await _schedule.AddAsync(days, start, end, ScheduleLabelInput.Text, true, _parentSession, DateTimeOffset.UtcNow);
            ScheduleLabelInput.Clear();
            ScheduleStatusText.Text = "Schedule saved.";
            await RefreshSchedules();
            await EvaluateSchedule(DateTimeOffset.UtcNow);
        }
        catch (Exception ex)
        {
            _logger.Error("Protection schedule could not be added.", ex);
            ScheduleStatusText.Text = "Schedule was not saved. Check the day mask (1-127), minute range, and overlaps.";
        }
    }

    private async void DeleteSchedule(object sender, RoutedEventArgs e)
    {
        if (!EnsureParentSession() || ScheduleList.SelectedItem is not ScheduleListItem selected) return;
        try
        {
            await _schedule.DeleteAsync(selected.Id, _parentSession, DateTimeOffset.UtcNow);
            ScheduleStatusText.Text = "Schedule deleted.";
            await RefreshSchedules();
            await EvaluateSchedule(DateTimeOffset.UtcNow);
        }
        catch (Exception ex)
        {
            _logger.Error("Protection schedule could not be deleted.", ex);
            ScheduleStatusText.Text = "Schedule could not be deleted.";
        }
    }

    private void OnScheduleSelectionChanged(object sender, SelectionChangedEventArgs e) =>
        OnScheduleSelected();

    private void OnScheduleSelected()
    {
        DeleteScheduleButton.IsEnabled = _parentSession.IsActive(DateTimeOffset.UtcNow) && ScheduleList.SelectedItem is ScheduleListItem;
        SaveScheduleButton.Content = ScheduleList.SelectedItem is ScheduleListItem ? "Save" : "Add";
        if (ScheduleList.SelectedItem is ScheduleListItem selected)
        {
            ScheduleDaysInput.Text = selected.Days.ToString();
            ScheduleStartInput.Text = selected.Start.ToString();
            ScheduleEndInput.Text = selected.End.ToString();
            ScheduleLabelInput.Text = selected.Note ?? string.Empty;
        }
    }

    private void ClearScheduleSelection(object sender, RoutedEventArgs e)
    {
        ScheduleList.SelectedIndex = -1;
        ScheduleLabelInput.Clear();
        SaveScheduleButton.Content = "Add";
    }

    private async Task RefreshSchedules()
    {
        var windows = await _schedule.GetWindowsAsync();
        ScheduleList.ItemsSource = windows.Select(window => new ScheduleListItem(window.Id,
            $"{(string.IsNullOrWhiteSpace(window.Label) ? "Protection" : window.Label)} — days {window.DaysMask}, {MinuteTime(window.StartMinutes)}–{MinuteTime(window.EndMinutes)}",
            window.DaysMask, window.StartMinutes, window.EndMinutes, window.Label)).ToArray();
        OnScheduleSelected();
    }

    private static string MinuteTime(int minutes) => minutes == 1440 ? "24:00" : $"{minutes / 60:00}:{minutes % 60:00}";

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
            var now = DateTimeOffset.UtcNow;
            var enabling = current.State != ProtectionState.On;
            await _protection.SetProtectionAsync(enabling, _parentSession, now);
            if (!enabling)
            {
                var until = await _schedule.OverrideCurrentWindowAsync(_parentSession, now);
                if (until is { } end)
                    ScheduleStatusText.Text = $"Protection is paused until this schedule ends at {end.ToLocalTime():t}. The next scheduled window will turn it on again.";
            }
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
        if (!_parentSession.RecordActivity(DateTimeOffset.UtcNow) && _page is Page.Parent or Page.Blocklist or Page.ChangePin)
            ExpireParentSession();
        else if (_page == Page.Parent)
            UpdateSessionCountdown();
    }

    private void OnTimerTick(object? sender, EventArgs e)
    {
        var now = DateTimeOffset.UtcNow;
        if (now - _lastScheduleEvaluationUtc >= TimeSpan.FromSeconds(30))
        {
            _lastScheduleEvaluationUtc = now;
            _ = EvaluateSchedule(now);
        }
        if (_page is Page.Parent or Page.Blocklist or Page.ChangePin)
        {
            if (!_parentSession.IsActive(DateTimeOffset.UtcNow)) ExpireParentSession();
            else if (_page == Page.Parent) UpdateSessionCountdown();
        }
        UpdateLockoutCountdown();
    }

    private async Task EvaluateSchedule(DateTimeOffset now)
    {
        try
        {
            var evaluation = await _schedule.EvaluateAsync(now);
            var status = await _protection.GetStatusAsync();
            var scheduledOn = evaluation.IsScheduled && !evaluation.IsOverridden;
            if (status.EngineAvailable && ((scheduledOn && status.State == ProtectionState.Off) ||
                (!scheduledOn && status.State == ProtectionState.On && status.ActiveTrigger == StudySessionTrigger.Schedule)))
            {
                await _protection.ApplyScheduledStateAsync(scheduledOn, now);
                status = await _protection.GetStatusAsync();
            }
            ScheduleStatusText.Text = evaluation.IsOverridden
                ? $"Schedule override active until {evaluation.OverrideUntilUtc:HH:mm} UTC."
                : evaluation.IsScheduled
                    ? $"Scheduled protection window active{(string.IsNullOrWhiteSpace(evaluation.ActiveLabel) ? "." : $": {evaluation.ActiveLabel}.")}"
                    : "No protection schedule window is active.";
            if (_page == Page.Parent) await RefreshProtectionStatus();
        }
        catch (Exception ex) { _logger.Error("Protection schedule evaluation failed.", ex); }
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
        BlocklistPanel.Visibility = page == Page.Blocklist ? Visibility.Visible : Visibility.Collapsed;
        ChangePinPanel.Visibility = page == Page.ChangePin ? Visibility.Visible : Visibility.Collapsed;
    }
}
