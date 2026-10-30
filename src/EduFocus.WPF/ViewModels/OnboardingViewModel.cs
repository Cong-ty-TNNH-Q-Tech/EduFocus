using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using EduFocus.Domain.Attention;
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
    private readonly Func<ICameraCheckUseCase> _cameraCheckFactory;
    private readonly ICameraPlatformInfo _platform;
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
    [ObservableProperty] private string _cameraStatusText =
        "EduFocus sẽ kiểm tra nhanh camera trước khi bật giám sát.";
    [ObservableProperty] private bool _cameraStatusIsWarning;
    [ObservableProperty] private bool _isCheckingCamera;
    [ObservableProperty] private bool _isBusy;
    [ObservableProperty] private bool _useRecoveryCodeOnResume;
    [ObservableProperty] private string _resumeRecoveryCode = string.Empty;
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
    public bool ShowResumeRecoveryInput => _credentialConfigured && UseRecoveryCodeOnResume;

    public bool CanContinuePrivacy => !IsBusy && PrivacyAccepted && GuardianAcknowledged;
    public bool CanFinish => !IsBusy && RecoveryCodeSaved && IssuedRecoveryCode.Length > 0;
    public bool CanCheckCamera => !IsBusy && !IsCheckingCamera;

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

    public int StepNumber => View switch
    {
        OnboardingStep.Welcome => 1,
        OnboardingStep.Privacy => 2,
        OnboardingStep.Preferences => 3,
        OnboardingStep.Pin => 4,
        OnboardingStep.ResumePin => 4,
        OnboardingStep.RecoveryCode => 5,
        _ => 0
    };

    public double StepProgress => StepNumber * 20;

    public event EventHandler? PasswordBoxesShouldClear;
    public event EventHandler? CloseRequested;
    public event EventHandler? Completed;

    public OnboardingViewModel(
        IOnboardingUseCase onboarding,
        Func<ICameraCheckUseCase> cameraCheckFactory,
        ICameraPlatformInfo platform,
        DatabaseBootstrapper database,
        IAppLogger logger)
    {
        _onboarding = onboarding ?? throw new ArgumentNullException(nameof(onboarding));
        _cameraCheckFactory = cameraCheckFactory ?? throw new ArgumentNullException(nameof(cameraCheckFactory));
        _platform = platform ?? throw new ArgumentNullException(nameof(platform));
        _database = database ?? throw new ArgumentNullException(nameof(database));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    partial void OnPrivacyAcceptedChanged(bool value) => OnPropertyChanged(nameof(CanContinuePrivacy));

    partial void OnGuardianAcknowledgedChanged(bool value) => OnPropertyChanged(nameof(CanContinuePrivacy));

    partial void OnRecoveryCodeSavedChanged(bool value) => OnPropertyChanged(nameof(CanFinish));

    partial void OnIssuedRecoveryCodeChanged(string value) => OnPropertyChanged(nameof(CanFinish));

    partial void OnIsCheckingCameraChanged(bool value) => OnPropertyChanged(nameof(CanCheckCamera));

    partial void OnIsBusyChanged(bool value)
    {
        OnPropertyChanged(nameof(CanContinuePrivacy));
        OnPropertyChanged(nameof(CanFinish));
        OnPropertyChanged(nameof(CanCheckCamera));
    }

    partial void OnUseRecoveryCodeOnResumeChanged(bool value) =>
        OnPropertyChanged(nameof(ShowResumeRecoveryInput));

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
        OnPropertyChanged(nameof(StepNumber));
        OnPropertyChanged(nameof(StepProgress));
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
            SetStatus("Chọn tuỳ chọn phù hợp với gia đình.", false);
            await CheckCameraAsync();
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
        if (IsBusy)
            return;

        if (!CanContinuePrivacy)
        {
            SetStatus("Bạn cần xác nhận cả hai mục trước khi tiếp tục.", true);
            return;
        }

        IsBusy = true;
        SetStatus("Đang lưu xác nhận quyền riêng tư...", false);

        try
        {
            await _onboarding.RecordPrivacyConsentAsync();

            View = OnboardingStep.Preferences;
            SetStatus("Chọn tuỳ chọn phù hợp với gia đình.", false);
            await CheckCameraAsync();
        }
        catch (Exception ex)
        {
            _logger.Error(LogCategories.UI, "Could not record onboarding privacy consent.", ex);
            SetStatus("Không thể lưu xác nhận. Kiểm tra quyền truy cập dữ liệu và thử lại.", true);
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    private async Task ContinuePreferencesAsync()
    {
        if (IsBusy)
            return;

        IsBusy = true;
        SetStatus("Đang lưu tuỳ chọn...", false);

        try
        {
            await _onboarding.SavePreferencesAsync(
                new OnboardingPreferences(MonitoringEnabled, StartWithWindows));

            if (_credentialConfigured)
            {
                UseRecoveryCodeOnResume = false;
                View = OnboardingStep.ResumePin;
                SetStatus("Nhập PIN phụ huynh để tạo lại mã khôi phục.", false);
                return;
            }

            View = OnboardingStep.Pin;
            SetStatus("Tạo PIN phụ huynh để bảo vệ các cài đặt quan trọng.", false);
        }
        catch (Exception ex)
        {
            _logger.Error(LogCategories.UI, "Could not save onboarding preferences.", ex);
            SetStatus("Không thể lưu tuỳ chọn. Kiểm tra quyền truy cập dữ liệu và thử lại.", true);
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    private void OpenResumeRecoveryReset()
    {
        if (IsBusy || !_credentialConfigured)
            return;

        ClearInputs();
        UseRecoveryCodeOnResume = true;
        OnPropertyChanged(nameof(ShowResumeRecoveryInput));
        View = OnboardingStep.Pin;
        SetStatus("Nhập mã khôi phục đã lưu và tạo PIN mới.", false);
    }

    [RelayCommand]
    private void BackToResumePin()
    {
        if (IsBusy || !_credentialConfigured)
            return;

        ClearInputs();
        UseRecoveryCodeOnResume = false;
        OnPropertyChanged(nameof(ShowResumeRecoveryInput));
        View = OnboardingStep.ResumePin;
        SetStatus("Nhập PIN phụ huynh để tiếp tục.", false);
    }

    [RelayCommand]
    private async Task OpenPreferencesAsync()
    {
        if (IsBusy)
            return;

        ClearInputs();
        View = OnboardingStep.Preferences;
        SetStatus("Chọn tuỳ chọn phù hợp với gia đình.", false);
        await CheckCameraAsync();
    }

    [RelayCommand]
    private async Task ConfigurePinAsync()
    {
        if (IsBusy || View != OnboardingStep.Pin)
            return;

        if (_credentialConfigured && UseRecoveryCodeOnResume && string.IsNullOrWhiteSpace(ResumeRecoveryCode))
        {
            SetStatus("Nhập mã khôi phục đã lưu để đặt lại PIN.", true);
            return;
        }

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
        var recoveryCode = ResumeRecoveryCode;
        var resetWithRecovery = _credentialConfigured && UseRecoveryCodeOnResume;
        ClearInputs();
        IsBusy = true;
        SetStatus(resetWithRecovery ? "Đang đặt lại PIN..." : "Đang tạo PIN...", false);

        try
        {
            if (resetWithRecovery)
            {
                var resetResult = await Task.Run(() => _onboarding.ResetPinWithRecoveryCodeAsync(recoveryCode, pin));
                switch (resetResult.Status)
                {
                    case ParentAuthStatus.Success when resetResult.RecoveryCode is not null:
                        _credentialConfigured = true;
                        UseRecoveryCodeOnResume = false;
                        IssuedRecoveryCode = resetResult.RecoveryCode;
                        RecoveryCodeSaved = false;
                        View = OnboardingStep.RecoveryCode;
                        SetStatus("PIN đã được đặt lại. Hãy lưu mã khôi phục mới trước khi hoàn tất.", false);
                        break;
                    case ParentAuthStatus.LockedOut:
                        SetStatus(LockoutMessage(resetResult.LockoutUntilUtc), true);
                        break;
                    case ParentAuthStatus.NotConfigured:
                        _credentialConfigured = false;
                        UseRecoveryCodeOnResume = false;
                        OnPropertyChanged(nameof(ShowResumeRecoveryInput));
                        SetStatus("Chưa có PIN phụ huynh trên máy này. Hãy tạo PIN mới.", true);
                        break;
                    default:
                        SetStatus($"Mã khôi phục không đúng. Còn {resetResult.RemainingAttempts} lần thử.", true);
                        break;
                }

                return;
            }

            var result = await Task.Run(() => _onboarding.ConfigurePinAsync(pin));
            _credentialConfigured = true;
            OnPropertyChanged(nameof(ShowResumeRecoveryInput));
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
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    private async Task ResumeWithPinAsync()
    {
        if (IsBusy || View != OnboardingStep.ResumePin)
            return;

        if (string.IsNullOrEmpty(ResumePin))
        {
            SetStatus("Nhập PIN phụ huynh.", true);
            return;
        }

        var pin = ResumePin;
        ClearInputs();
        IsBusy = true;
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
                    _credentialConfigured = false;
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
        finally
        {
            IsBusy = false;
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
        if (IsBusy)
            return;

        if (!CanFinish)
        {
            SetStatus("Xác nhận bạn đã lưu mã khôi phục trước khi hoàn tất.", true);
            return;
        }

        IsBusy = true;
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
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    private async Task CheckCameraAsync()
    {
        if (IsCheckingCamera)
            return;

        IsCheckingCamera = true;
        CameraStatusText = "Đang kiểm tra camera trong vài giây...";
        CameraStatusIsWarning = false;

        try
        {
            var result = await _cameraCheckFactory().RunAsync();
            var guidance = CameraGuidance.Describe(result, _platform.SupportsMultiAppCamera);

            CameraStatusText = guidance is null
                ? CameraGuidance.Summary(result)
                : $"{CameraGuidance.Summary(result)} {guidance}";
            CameraStatusIsWarning = !result.IsReady;
        }
        catch (Exception ex)
        {
            _logger.Error(LogCategories.Attention, "The onboarding camera check failed.", ex);
            CameraStatusText =
                "Không thể kiểm tra camera lúc này. Bạn vẫn có thể tiếp tục và kiểm tra lại trong Parent Mode.";
            CameraStatusIsWarning = true;
        }
        finally
        {
            IsCheckingCamera = false;
        }
    }

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
        ResumeRecoveryCode = string.Empty;
        PasswordBoxesShouldClear?.Invoke(this, EventArgs.Empty);
    }

    private void SetStatus(string message, bool isError)
    {
        StatusMessage = isError ? $"Lỗi: {message}" : message;
        StatusIsError = isError;
    }
}
