using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using EduFocus.Domain.Security;
using EduFocus.Ports.DTOs;
using EduFocus.Ports.Inbound;
using EduFocus.Ports.Outbound.Services;
using EduFocus.Ports.Protection;
using EduFocus.Ports.Security;
using EduFocus.WPF.Services;

namespace EduFocus.WPF.ViewModels;

public enum ParentModeView
{
    Loading,
    NotConfigured,
    Error,
    Login,
    Management,
    ChangePin,
    RecoveryReset,
    RecoveryCode
}

/// <summary>
/// Screen flow for Parent Mode (UC02, UC13): PIN login, session status,
/// PIN change, recovery reset and the one-time display of a new recovery code.
/// Secrets are handed to the use case and cleared from the view immediately.
/// </summary>
public partial class ParentModeViewModel : ObservableObject
{
    private const string PinPolicyMessage =
        "PIN phải có 6–8 chữ số, không lặp lại một chữ số và không tăng/giảm liên tiếp.";

    private readonly IParentModeUseCase _mode;
    private readonly ProtectionStatusViewModel _protection;
    private readonly ScheduleViewModel _schedule;
    private readonly BlocklistViewModel _blocklist;
    private readonly CameraStatusViewModel _camera;
    private readonly DashboardViewModel _dashboard;
    private readonly ReportViewModel _report;
    private readonly DatabaseBootstrapper _database;
    private readonly IAppLogger _logger;
    private readonly TimeProvider _timeProvider;

    private const int DashboardRefreshTicks = 15;

    private DateTime? _lockoutUntilUtc;
    private int _ticksSinceDashboardRefresh;

    [ObservableProperty] private ParentModeView _view = ParentModeView.Loading;
    [ObservableProperty] private string _statusMessage = string.Empty;
    [ObservableProperty] private bool _statusIsError;
    [ObservableProperty] private string _issuedRecoveryCode = string.Empty;

    public string LoginPin { get; set; } = string.Empty;
    public string CurrentPin { get; set; } = string.Empty;
    public string NewPin { get; set; } = string.Empty;
    public string ConfirmNewPin { get; set; } = string.Empty;
    public string RecoveryCode { get; set; } = string.Empty;
    public string ResetNewPin { get; set; } = string.Empty;
    public string ConfirmResetPin { get; set; } = string.Empty;

    public bool IsLoading => View == ParentModeView.Loading;
    public bool IsNotConfigured => View == ParentModeView.NotConfigured;
    public bool IsError => View == ParentModeView.Error;
    public bool IsLoginVisible => View == ParentModeView.Login;
    public bool IsManagementVisible => View == ParentModeView.Management;
    public bool IsChangePinVisible => View == ParentModeView.ChangePin;
    public bool IsRecoveryResetVisible => View == ParentModeView.RecoveryReset;
    public bool IsRecoveryCodeVisible => View == ParentModeView.RecoveryCode;

    public ProtectionStatusViewModel Protection => _protection;

    public ScheduleViewModel Schedule => _schedule;

    public BlocklistViewModel Blocklist => _blocklist;

    public CameraStatusViewModel Camera => _camera;

    public DashboardViewModel Dashboard => _dashboard;

    public ReportViewModel Report => _report;

    public event EventHandler? PasswordBoxesShouldClear;
    public event EventHandler? CloseRequested;

    public ParentModeViewModel(
        IParentModeUseCase mode,
        ProtectionStatusViewModel protection,
        ScheduleViewModel schedule,
        BlocklistViewModel blocklist,
        CameraStatusViewModel camera,
        DashboardViewModel dashboard,
        ReportViewModel report,
        DatabaseBootstrapper database,
        IAppLogger logger,
        TimeProvider timeProvider)
    {
        _mode = mode ?? throw new ArgumentNullException(nameof(mode));
        _protection = protection ?? throw new ArgumentNullException(nameof(protection));
        _schedule = schedule ?? throw new ArgumentNullException(nameof(schedule));
        _blocklist = blocklist ?? throw new ArgumentNullException(nameof(blocklist));
        _camera = camera ?? throw new ArgumentNullException(nameof(camera));
        _dashboard = dashboard ?? throw new ArgumentNullException(nameof(dashboard));
        _report = report ?? throw new ArgumentNullException(nameof(report));
        _database = database ?? throw new ArgumentNullException(nameof(database));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _timeProvider = timeProvider ?? throw new ArgumentNullException(nameof(timeProvider));
    }

    partial void OnViewChanged(ParentModeView value)
    {
        OnPropertyChanged(nameof(IsLoading));
        OnPropertyChanged(nameof(IsNotConfigured));
        OnPropertyChanged(nameof(IsError));
        OnPropertyChanged(nameof(IsLoginVisible));
        OnPropertyChanged(nameof(IsManagementVisible));
        OnPropertyChanged(nameof(IsChangePinVisible));
        OnPropertyChanged(nameof(IsRecoveryResetVisible));
        OnPropertyChanged(nameof(IsRecoveryCodeVisible));
    }

    [RelayCommand]
    private async Task LoadAsync()
    {
        ClearInputs();
        _lockoutUntilUtc = null;
        SetStatus("Đang chuẩn bị dữ liệu...", false);
        View = ParentModeView.Loading;

        try
        {
            await Task.Run(() => _database.EnsureInitializedAsync());

            if (!await _mode.IsConfiguredAsync())
            {
                View = ParentModeView.NotConfigured;
                SetStatus("Parent Mode chưa được thiết lập trên máy này.", false);
                return;
            }

            if (_mode.IsAuthenticated)
            {
                await _protection.RefreshAsync();
                await _schedule.RefreshAsync();
                await _blocklist.RefreshAsync();
                await RefreshCameraAsync();
                await _dashboard.RefreshAsync();
                await _report.RefreshAsync();
                _ticksSinceDashboardRefresh = 0;
                View = ParentModeView.Management;
                SetStatus("Parent Mode đang mở. Phiên tự khóa sau 5 phút không hoạt động.", false);
            }
            else
            {
                View = ParentModeView.Login;
                SetStatus("Nhập PIN phụ huynh để tiếp tục.", false);
            }
        }
        catch (Exception ex)
        {
            _logger.Error(LogCategories.UI, "Could not open the Parent Mode database.", ex);
            View = ParentModeView.Error;
            SetStatus(
                "Không thể mở dữ liệu Parent Mode. Hãy chạy EduFocus với quyền quản trị và thử lại.",
                true);
        }
    }

    [RelayCommand]
    private async Task LoginAsync()
    {
        if (View != ParentModeView.Login)
            return;

        if (string.IsNullOrEmpty(LoginPin))
        {
            SetStatus("Nhập PIN phụ huynh.", true);
            return;
        }

        var pin = LoginPin;
        ClearInputs();
        SetStatus("Đang xác thực...", false);

        try
        {
            var result = await Task.Run(() => _mode.LoginAsync(pin));
            await ApplyLoginResultAsync(result);
        }
        catch (Exception ex)
        {
            _logger.Error(LogCategories.Security, "Parent PIN verification failed unexpectedly.", ex);
            SetStatus("Không thể xác thực lúc này. Vui lòng thử lại.", true);
        }
    }

    [RelayCommand]
    private void OpenChangePin()
    {
        if (!TryTouchSession())
            return;

        ClearInputs();
        SetStatus("Nhập PIN hiện tại, PIN mới và xác nhận PIN mới.", false);
        View = ParentModeView.ChangePin;
    }

    [RelayCommand]
    private void OpenRecoveryReset()
    {
        _mode.Lock();
        ClearInputs();
        SetStatus("Nhập mã khôi phục và PIN mới. PIN cũ sẽ bị thay thế.", false);
        View = ParentModeView.RecoveryReset;
    }

    [RelayCommand]
    private async Task ChangePinAsync()
    {
        if (View != ParentModeView.ChangePin)
            return;

        if (string.IsNullOrEmpty(CurrentPin))
        {
            SetStatus("Nhập PIN hiện tại.", true);
            return;
        }

        if (!ValidateNewPin(NewPin, ConfirmNewPin, out var validationMessage))
        {
            SetStatus(validationMessage, true);
            return;
        }

        var current = CurrentPin;
        var newPin = NewPin;
        ClearInputs();
        SetStatus("Đang đổi PIN...", false);

        try
        {
            var result = await Task.Run(() => _mode.ChangePinAsync(current, newPin));

            switch (result.Status)
            {
                case ParentAuthStatus.Success:
                    View = ParentModeView.Login;
                    SetStatus("Đã đổi PIN. Vui lòng đăng nhập lại bằng PIN mới.", false);
                    break;
                case ParentAuthStatus.LockedOut:
                    ApplyLockout(result.LockoutUntilUtc);
                    break;
                case ParentAuthStatus.NotConfigured:
                    View = ParentModeView.NotConfigured;
                    SetStatus("Parent Mode chưa được thiết lập trên máy này.", false);
                    break;
                default:
                    SetStatus(
                        $"PIN hiện tại không đúng. Còn {result.RemainingAttempts} lần thử.",
                        true);
                    break;
            }
        }
        catch (ParentAuthorizationException)
        {
            View = ParentModeView.Login;
            SetStatus("Phiên Parent Mode đã hết hạn. Vui lòng đăng nhập lại.", true);
        }
        catch (ArgumentException)
        {
            SetStatus(PinPolicyMessage, true);
        }
        catch (Exception ex)
        {
            _logger.Error(LogCategories.Security, "Parent PIN change failed unexpectedly.", ex);
            SetStatus("Không thể đổi PIN lúc này. Vui lòng thử lại.", true);
        }
    }

    [RelayCommand]
    private async Task ResetPinAsync()
    {
        if (View != ParentModeView.RecoveryReset)
            return;

        if (string.IsNullOrWhiteSpace(RecoveryCode))
        {
            SetStatus("Nhập mã khôi phục.", true);
            return;
        }

        if (!ValidateNewPin(ResetNewPin, ConfirmResetPin, out var validationMessage))
        {
            SetStatus(validationMessage, true);
            return;
        }

        var recoveryCode = RecoveryCode;
        var newPin = ResetNewPin;
        ClearInputs();
        SetStatus("Đang đặt lại PIN...", false);

        try
        {
            var result = await Task.Run(() => _mode.ResetPinWithRecoveryCodeAsync(recoveryCode, newPin));

            switch (result.Status)
            {
                case ParentAuthStatus.Success when result.RecoveryCode is not null:
                    IssuedRecoveryCode = result.RecoveryCode;
                    View = ParentModeView.RecoveryCode;
                    SetStatus("PIN đã được đặt lại. Hãy lưu mã khôi phục mới.", false);
                    break;
                case ParentAuthStatus.Success:
                    View = ParentModeView.Login;
                    SetStatus("PIN đã được đặt lại. Vui lòng đăng nhập bằng PIN mới.", false);
                    break;
                case ParentAuthStatus.LockedOut:
                    ApplyLockout(result.LockoutUntilUtc);
                    break;
                case ParentAuthStatus.NotConfigured:
                    View = ParentModeView.NotConfigured;
                    SetStatus("Parent Mode chưa được thiết lập trên máy này.", false);
                    break;
                default:
                    SetStatus(
                        $"Mã khôi phục không đúng. Còn {result.RemainingAttempts} lần thử.",
                        true);
                    break;
            }
        }
        catch (ArgumentException)
        {
            SetStatus(PinPolicyMessage, true);
        }
        catch (Exception ex)
        {
            _logger.Error(LogCategories.Security, "Parent PIN reset failed unexpectedly.", ex);
            SetStatus("Không thể đặt lại PIN lúc này. Vui lòng thử lại.", true);
        }
    }

    [RelayCommand]
    private void AcknowledgeRecoveryCode()
    {
        IssuedRecoveryCode = string.Empty;
        ClearInputs();
        View = ParentModeView.Login;
        SetStatus("Vui lòng đăng nhập bằng PIN mới.", false);
    }

    [RelayCommand]
    private void CancelChangePin()
    {
        ClearInputs();

        if (_mode.IsAuthenticated)
        {
            View = ParentModeView.Management;
            SetStatus("Đã hủy đổi PIN.", false);
        }
        else
        {
            View = ParentModeView.Login;
            SetStatus("Phiên Parent Mode đã hết hạn. Vui lòng đăng nhập lại.", true);
        }
    }

    [RelayCommand]
    private void BackToLogin()
    {
        _mode.Lock();
        ClearInputs();
        View = ParentModeView.Login;
        SetStatus("Nhập PIN phụ huynh để tiếp tục.", false);
    }

    [RelayCommand]
    private void LockSession()
    {
        _mode.Lock();
        ClearInputs();
        View = ParentModeView.Login;
        SetStatus("Đã khóa Parent Mode.", false);
    }

    [RelayCommand]
    private async Task ToggleProtectionAsync()
    {
        if (View != ParentModeView.Management || !TryTouchSession())
            return;

        SetStatus("Đang cập nhật trạng thái bảo vệ...", false);

        try
        {
            var result = await _protection.ToggleAsync();
            await _schedule.RefreshAsync();
            await _dashboard.RefreshAsync();
            var state = result.Status.IsEnabled ? "bật" : "tắt";

            SetStatus(
                result.Changed
                    ? $"Đã {state} bảo vệ."
                    : $"Bảo vệ đã ở trạng thái {state}.",
                false);
        }
        catch (ParentAuthorizationException)
        {
            _mode.Lock();
            ClearInputs();
            View = ParentModeView.Login;
            SetStatus("Phiên Parent Mode đã hết hạn. Vui lòng đăng nhập lại.", true);
        }
        catch (ProtectionEngineException ex)
        {
            _logger.Error(LogCategories.Protection, "Protection toggle failed.", ex);
            await _protection.RefreshAsync();
            SetStatus(
                ex.Operation == "remove"
                    ? "Không thể gỡ engine bảo vệ nên trạng thái vẫn là BẬT. Vui lòng thử lại."
                    : "Không thể áp dụng engine bảo vệ nên trạng thái vẫn là TẮT. Vui lòng thử lại.",
                true);
        }
        catch (Exception ex)
        {
            _logger.Error(LogCategories.Protection, "Protection toggle failed unexpectedly.", ex);
            await _protection.RefreshAsync();
            SetStatus("Không thể đổi trạng thái bảo vệ lúc này. Vui lòng thử lại.", true);
        }
    }

    [RelayCommand]
    private void Close()
    {
        _mode.Lock();
        ClearInputs();
        CloseRequested?.Invoke(this, EventArgs.Empty);
    }

    public void HandleTick()
    {
        if (View is ParentModeView.Management or ParentModeView.ChangePin && !_mode.IsAuthenticated)
        {
            ClearInputs();
            View = ParentModeView.Login;
            SetStatus(
                "Phiên Parent Mode đã hết hạn sau 5 phút không hoạt động. Vui lòng đăng nhập lại.",
                true);
            return;
        }

        if (_lockoutUntilUtc is not null && View == ParentModeView.Login)
            UpdateLockoutStatus();

        // UC10 polling: the dashboard timer ticks every second, the data read
        // runs every 15 seconds while the management view is visible.
        if (View == ParentModeView.Management
            && ++_ticksSinceDashboardRefresh >= DashboardRefreshTicks)
        {
            _ticksSinceDashboardRefresh = 0;
            _ = RefreshLiveAsync();
        }
    }

    public void HandleWindowClosed()
    {
        _mode.Lock();
        IssuedRecoveryCode = string.Empty;
        ClearInputs();
    }

    private async Task ApplyLoginResultAsync(ParentAuthResult result)
    {
        switch (result.Status)
        {
            case ParentAuthStatus.Success:
                _lockoutUntilUtc = null;
                await _protection.RefreshAsync();
                await _schedule.RefreshAsync();
                await _blocklist.RefreshAsync();
                await RefreshCameraAsync();
                await _dashboard.RefreshAsync();
                await _report.RefreshAsync();
                _ticksSinceDashboardRefresh = 0;
                View = ParentModeView.Management;
                SetStatus("Đã mở Parent Mode. Phiên tự khóa sau 5 phút không hoạt động.", false);
                break;
            case ParentAuthStatus.LockedOut:
                ApplyLockout(result.LockoutUntilUtc);
                break;
            case ParentAuthStatus.NotConfigured:
                View = ParentModeView.NotConfigured;
                SetStatus("Parent Mode chưa được thiết lập trên máy này.", false);
                break;
            default:
                SetStatus($"PIN không đúng. Còn {result.RemainingAttempts} lần thử.", true);
                break;
        }
    }

    private async Task RefreshCameraAsync()
    {
        _camera.Refresh();
        await _camera.LoadDevicesAsync();
    }

    private async Task RefreshLiveAsync()
    {
        await _protection.RefreshAsync();
        await _dashboard.RefreshAsync();
    }

    private void ApplyLockout(DateTime? lockoutUntilUtc)
    {
        _mode.Lock();
        View = ParentModeView.Login;
        _lockoutUntilUtc = lockoutUntilUtc;

        if (lockoutUntilUtc is null)
            SetStatus("Đăng nhập tạm khóa. Vui lòng thử lại sau.", true);
        else
            UpdateLockoutStatus();
    }

    private void UpdateLockoutStatus()
    {
        if (_lockoutUntilUtc is not { } until)
            return;

        var remaining = until - _timeProvider.GetUtcNow().UtcDateTime;
        if (remaining <= TimeSpan.Zero)
        {
            _lockoutUntilUtc = null;
            if (View == ParentModeView.Login)
                SetStatus("Bạn có thể thử lại PIN.", false);
            return;
        }

        var minutes = (int)Math.Ceiling(remaining.TotalMinutes);
        SetStatus($"Đã tạm khóa đăng nhập. Vui lòng thử lại sau {minutes} phút.", true);
    }

    private bool TryTouchSession()
    {
        _mode.RecordActivity();

        if (_mode.IsAuthenticated)
            return true;

        View = ParentModeView.Login;
        SetStatus("Phiên Parent Mode đã hết hạn. Vui lòng đăng nhập lại.", true);
        return false;
    }

    private static bool ValidateNewPin(string newPin, string confirmPin, out string message)
    {
        if (!ParentPinPolicy.IsValid(newPin))
        {
            message = PinPolicyMessage;
            return false;
        }

        if (!string.Equals(newPin, confirmPin, StringComparison.Ordinal))
        {
            message = "PIN xác nhận không khớp.";
            return false;
        }

        message = string.Empty;
        return true;
    }

    private void ClearInputs()
    {
        LoginPin = string.Empty;
        CurrentPin = string.Empty;
        NewPin = string.Empty;
        ConfirmNewPin = string.Empty;
        RecoveryCode = string.Empty;
        ResetNewPin = string.Empty;
        ConfirmResetPin = string.Empty;
        PasswordBoxesShouldClear?.Invoke(this, EventArgs.Empty);
    }

    private void SetStatus(string message, bool isError)
    {
        StatusMessage = message;
        StatusIsError = isError;
    }
}
