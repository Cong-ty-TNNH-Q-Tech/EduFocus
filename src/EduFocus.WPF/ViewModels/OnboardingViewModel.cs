using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using EduFocus.Domain.Security;
using EduFocus.Ports.DTOs;
using EduFocus.Ports.Inbound;
using EduFocus.Ports.Outbound.Services;
using EduFocus.WPF.Services;

namespace EduFocus.WPF.ViewModels;

public enum OnboardingStep
{
    Loading,
    Error,
    Welcome,
    Privacy,
    Preferences,
    Pin,
    ResumePin,
    RecoveryCode
}

/// <summary>
/// First-run wizard (UC01): privacy consent, safe preferences, parent PIN and
/// the one-time recovery code. Every completed step is persisted, so closing
/// the app early resumes without losing work. Secrets clear from the view
/// immediately after they are submitted.
/// </summary>
public partial class OnboardingViewModel : ObservableObject
{
    private const string PinPolicyMessage =
        "PIN phải có 6–8 chữ số, không lặp lại một chữ số và không tăng/giảm liên tiếp.";

    private readonly IOnboardingUseCase _onboarding;
    private readonly DatabaseBootstrapper _database;
    private readonly IAppLogger _logger;

    private bool _credentialConfigured;

    [ObservableProperty] private OnboardingStep _view = OnboardingStep.Loading;
    [ObservableProperty] private string _statusMessage = string.Empty;
    [ObservableProperty] private bool _statusIsError;
    [ObservableProperty] private bool _privacyAccepted;
    [ObservableProperty] private bool _guardianAcknowledged;
    [ObservableProperty] private bool _monitoringEnabled = true;
    [ObservableProperty] private bool _startWithWindows = true;
    [ObservableProperty] private string _cameraStatusText = string.Empty;
    [ObservableProperty] private bool _cameraStatusIsWarning;
    [ObservableProperty] private string _issuedRecoveryCode = string.Empty;
    [ObservableProperty] private bool _recoveryCodeSaved;

    public string Pin { get; set; } = string.Empty;
    public string ConfirmPin { get; set; } = string.Empty;
    public string ResumePin { get; set; } = string.Empty;

    public bool IsLoading => View == OnboardingStep.Loading;
    public bool IsError => View == OnboardingStep.Error;
    public bool IsWelcomeVisible => View == OnboardingStep.Welcome;
    public bool IsPrivacyVisible => View == OnboardingStep.Privacy;
    public bool IsPreferencesVisible => View == OnboardingStep.Preferences;
    public bool IsPinVisible => View == OnboardingStep.Pin;
    public bool IsResumePinVisible => View == OnboardingStep.ResumePin;
    public bool IsRecoveryCodeVisible => View == OnboardingStep.RecoveryCode;

    public bool CanContinuePrivacy => PrivacyAccepted && GuardianAcknowledged;
    public bool CanFinish => RecoveryCodeSaved && IssuedRecoveryCode.Length > 0;

    public string StepLabel => View switch
    {
        OnboardingStep.Welcome => "Bước 1/5 · Giới thiệu",
        OnboardingStep.Privacy => "Bước 2/5 · Quyền riêng tư",
        OnboardingStep.Preferences => "Bước 3/5 · Tuỳ chọn an toàn",
        OnboardingStep.Pin => "Bước 4/5 · PIN phụ huynh",
        OnboardingStep.RecoveryCode => "Bước 5/5 · Mã khôi phục",
        OnboardingStep.ResumePin => "Tiếp tục · Xác nhận PIN",
        OnboardingStep.Error => "Khởi động",
        _ => string.Empty
    };

    public event EventHandler? PasswordBoxesShouldClear;
    public event EventHandler? CloseRequested;
    public event EventHandler? Completed;

    public OnboardingViewModel(
        IOnboardingUseCase onboarding,
        DatabaseBootstrapper database,
        IAppLogger logger)
    {
        _onboarding = onboarding ?? throw new ArgumentNullException(nameof(onboarding));
        _database = database ?? throw new ArgumentNullException(nameof(database));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    partial void OnPrivacyAcceptedChanged(bool value) => OnPropertyChanged(nameof(CanContinuePrivacy));

    partial void OnGuardianAcknowledgedChanged(bool value) => OnPropertyChanged(nameof(CanContinuePrivacy));

    partial void OnRecoveryCodeSavedChanged(bool value) => OnPropertyChanged(nameof(CanFinish));

    partial void OnIssuedRecoveryCodeChanged(string value) => OnPropertyChanged(nameof(CanFinish));

    partial void OnViewChanged(OnboardingStep value)
    {
        OnPropertyChanged(nameof(IsLoading));
        OnPropertyChanged(nameof(IsError));
        OnPropertyChanged(nameof(IsWelcomeVisible));
        OnPropertyChanged(nameof(IsPrivacyVisible));
        OnPropertyChanged(nameof(IsPreferencesVisible));
        OnPropertyChanged(nameof(IsPinVisible));
        OnPropertyChanged(nameof(IsResumePinVisible));
        OnPropertyChanged(nameof(IsRecoveryCodeVisible));
        OnPropertyChanged(nameof(StepLabel));
    }

    [RelayCommand]
    private async Task LoadAsync()
    {
        ClearInputs();
        View = OnboardingStep.Loading;
        SetStatus("Đang chuẩn bị dữ liệu trên máy này...", false);

        try
        {
            await Task.Run(() => _database.EnsureInitializedAsync());

            var state = await _onboarding.GetStateAsync();
            _credentialConfigured = state.CredentialConfigured;
            PrivacyAccepted = state.PrivacyConsentRecorded;
            GuardianAcknowledged = state.PrivacyConsentRecorded;
            MonitoringEnabled = state.Preferences.MonitoringEnabled;
            StartWithWindows = state.Preferences.StartWithWindows;

            if (state.OnboardingCompleted)
            {
                Completed?.Invoke(this, EventArgs.Empty);
                return;
            }

            if (!state.PrivacyConsentRecorded)
            {
                View = OnboardingStep.Welcome;
                SetStatus("Chào mừng bạn đến với EduFocus.", false);
                return;
            }

            if (state.CredentialConfigured)
            {
                View = OnboardingStep.ResumePin;
                SetStatus("Thiết lập trước đó chưa hoàn tất. Nhập PIN phụ huynh để tiếp tục.", false);
                return;
            }

            View = OnboardingStep.Preferences;
            UpdateCameraStatus();
            SetStatus("Chọn tuỳ chọn phù hợp với gia đình.", false);
        }
        catch (Exception ex)
        {
            _logger.Error(LogCategories.UI, "Could not start onboarding.", ex);
            View = OnboardingStep.Error;
            SetStatus(
                "Không thể mở dữ liệu EduFocus. Hãy chạy ứng dụng với quyền quản trị và thử lại.",
                true);
        }
    }

    [RelayCommand]
    private void ContinueWelcome()
    {
        View = OnboardingStep.Privacy;
        SetStatus("Xác nhận hai mục dưới đây để tiếp tục.", false);
    }

    [RelayCommand]
    private async Task ContinuePrivacyAsync()
    {
        if (!CanContinuePrivacy)
        {
            SetStatus("Bạn cần xác nhận cả hai mục trước khi tiếp tục.", true);
            return;
        }

        SetStatus("Đang lưu xác nhận quyền riêng tư...", false);

        try
        {
            await _onboarding.RecordPrivacyConsentAsync();

            if (_credentialConfigured)
            {
                View = OnboardingStep.ResumePin;
                SetStatus("Nhập PIN phụ huynh để tạo lại mã khôi phục.", false);
                return;
            }

            View = OnboardingStep.Preferences;
            UpdateCameraStatus();
            SetStatus("Chọn tuỳ chọn phù hợp với gia đình.", false);
        }
        catch (Exception ex)
        {
            _logger.Error(LogCategories.UI, "Could not record onboarding privacy consent.", ex);
            SetStatus("Không thể lưu xác nhận. Kiểm tra quyền truy cập dữ liệu và thử lại.", true);
        }
    }

    [RelayCommand]
    private async Task ContinuePreferencesAsync()
    {
        SetStatus("Đang lưu tuỳ chọn...", false);

        try
        {
            await _onboarding.SavePreferencesAsync(
                new OnboardingPreferences(MonitoringEnabled, StartWithWindows));

            View = OnboardingStep.Pin;
            SetStatus("Tạo PIN phụ huynh để bảo vệ các cài đặt quan trọng.", false);
        }
        catch (Exception ex)
        {
            _logger.Error(LogCategories.UI, "Could not save onboarding preferences.", ex);
            SetStatus("Không thể lưu tuỳ chọn. Kiểm tra quyền truy cập dữ liệu và thử lại.", true);
        }
    }

    [RelayCommand]
    private async Task ConfigurePinAsync()
    {
        if (View != OnboardingStep.Pin)
            return;

        if (!ParentPinPolicy.IsValid(Pin))
        {
            SetStatus(PinPolicyMessage, true);
            return;
        }

        if (!string.Equals(Pin, ConfirmPin, StringComparison.Ordinal))
        {
            SetStatus("PIN xác nhận không khớp.", true);
            return;
        }

        var pin = Pin;
        ClearInputs();
        SetStatus("Đang tạo PIN...", false);

        try
        {
            var result = await Task.Run(() => _onboarding.ConfigurePinAsync(pin));
            _credentialConfigured = true;
            IssuedRecoveryCode = result.RecoveryCode;
            RecoveryCodeSaved = false;
            View = OnboardingStep.RecoveryCode;
            SetStatus("PIN đã được tạo. Hãy lưu mã khôi phục trước khi hoàn tất.", false);
        }
        catch (ArgumentException)
        {
            SetStatus(PinPolicyMessage, true);
        }
        catch (InvalidOperationException)
        {
            SetStatus("Chưa thể tạo PIN. Hãy khởi động lại ứng dụng để tiếp tục thiết lập.", true);
        }
        catch (Exception ex)
        {
            _logger.Error(LogCategories.Security, "Could not configure the onboarding PIN.", ex);
            SetStatus("Không thể lưu PIN. Kiểm tra quyền truy cập dữ liệu và thử lại.", true);
        }
    }

    [RelayCommand]
    private async Task ResumeWithPinAsync()
    {
        if (View != OnboardingStep.ResumePin)
            return;

        if (string.IsNullOrEmpty(ResumePin))
        {
            SetStatus("Nhập PIN phụ huynh.", true);
            return;
        }

        var pin = ResumePin;
        ClearInputs();
        SetStatus("Đang xác thực PIN...", false);

        try
        {
            var result = await Task.Run(() => _onboarding.ReissueRecoveryCodeAsync(pin));

            switch (result.Status)
            {
                case ParentAuthStatus.Success when result.RecoveryCode is not null:
                    IssuedRecoveryCode = result.RecoveryCode;
                    RecoveryCodeSaved = false;
                    View = OnboardingStep.RecoveryCode;
                    SetStatus("Đã tạo mã khôi phục mới. Mã cũ không còn hiệu lực.", false);
                    break;
                case ParentAuthStatus.LockedOut:
                    SetStatus(LockoutMessage(result.LockoutUntilUtc), true);
                    break;
                case ParentAuthStatus.NotConfigured:
                    View = OnboardingStep.Welcome;
                    SetStatus("Chưa có PIN phụ huynh trên máy này. Hãy thiết lập lại.", true);
                    break;
                default:
                    SetStatus($"PIN không đúng. Còn {result.RemainingAttempts} lần thử.", true);
                    break;
            }
        }
        catch (InvalidOperationException)
        {
            SetStatus("Chưa thể tạo lại mã khôi phục. Hãy khởi động lại ứng dụng.", true);
        }
        catch (Exception ex)
        {
            _logger.Error(LogCategories.Security, "Could not reissue the onboarding recovery code.", ex);
            SetStatus("Không thể xác thực PIN lúc này. Vui lòng thử lại.", true);
        }
    }

    [RelayCommand]
    private void CopyRecoveryCode()
    {
        if (string.IsNullOrEmpty(IssuedRecoveryCode))
            return;

        try
        {
            Clipboard.SetText(IssuedRecoveryCode);
            SetStatus("Đã sao chép mã khôi phục vào clipboard.", false);
        }
        catch (Exception ex)
        {
            _logger.Warning(
                LogCategories.UI,
                $"Could not copy the recovery code to the clipboard ({ex.GetType().Name}).");
            SetStatus("Không thể sao chép tự động. Hãy chọn mã trong ô và sao chép thủ công.", true);
        }
    }

    [RelayCommand]
    private async Task FinishAsync()
    {
        if (!CanFinish)
        {
            SetStatus("Xác nhận bạn đã lưu mã khôi phục trước khi hoàn tất.", true);
            return;
        }

        SetStatus("Đang hoàn tất thiết lập...", false);

        try
        {
            await _onboarding.CompleteAsync();
            IssuedRecoveryCode = string.Empty;
            ClearInputs();
            Completed?.Invoke(this, EventArgs.Empty);
        }
        catch (Exception ex)
        {
            _logger.Error(LogCategories.UI, "Could not complete onboarding.", ex);
            SetStatus("Không thể hoàn tất thiết lập. Kiểm tra quyền truy cập dữ liệu và thử lại.", true);
        }
    }

    [RelayCommand]
    private void CheckCamera() => UpdateCameraStatus();

    [RelayCommand]
    private void Exit()
    {
        ClearInputs();
        CloseRequested?.Invoke(this, EventArgs.Empty);
    }

    public void HandleWindowClosed()
    {
        IssuedRecoveryCode = string.Empty;
        ClearInputs();
    }

    private void UpdateCameraStatus()
    {
        switch (CameraPermissionProbe.Check())
        {
            case CameraPermissionState.Denied:
                CameraStatusText =
                    "Windows đang chặn ứng dụng dùng camera. Mở Settings → Privacy & security → Camera để cho phép trước khi bật giám sát.";
                CameraStatusIsWarning = true;
                break;
            case CameraPermissionState.Allowed:
                CameraStatusText =
                    "Windows đã cho phép camera. EduFocus sẽ kiểm tra thiết bị khi phiên giám sát bắt đầu.";
                CameraStatusIsWarning = false;
                break;
            default:
                CameraStatusText =
                    "Chưa xác định được quyền camera. EduFocus sẽ kiểm tra lại khi phiên giám sát bắt đầu.";
                CameraStatusIsWarning = false;
                break;
        }
    }

    private static string LockoutMessage(DateTime? lockoutUntilUtc)
    {
        if (lockoutUntilUtc is null)
            return "Đăng nhập đang tạm khóa. Vui lòng thử lại sau.";

        return $"Đăng nhập tạm khóa đến {lockoutUntilUtc.Value.ToLocalTime():HH:mm}. Vui lòng thử lại sau.";
    }

    private void ClearInputs()
    {
        Pin = string.Empty;
        ConfirmPin = string.Empty;
        ResumePin = string.Empty;
        PasswordBoxesShouldClear?.Invoke(this, EventArgs.Empty);
    }

    private void SetStatus(string message, bool isError)
    {
        StatusMessage = isError ? $"Lỗi: {message}" : message;
        StatusIsError = isError;
    }
}
