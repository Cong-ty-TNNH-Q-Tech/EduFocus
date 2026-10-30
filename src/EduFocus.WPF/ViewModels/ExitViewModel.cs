using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using EduFocus.Ports.DTOs;
using EduFocus.Ports.Inbound;
using EduFocus.Ports.Outbound.Services;
using EduFocus.WPF.Services;

namespace EduFocus.WPF.ViewModels;

/// <summary>
/// PIN-gated exit flow (UC12). The PIN is required even when protection is OFF;
/// when protection is ON the parent must additionally acknowledge that exiting
/// removes protection before the exit can be confirmed.
/// </summary>
public sealed partial class ExitViewModel : ObservableObject
{
    private readonly IAppLifecycleUseCase _lifecycle;
    private readonly ProtectionStatusViewModel _protection;
    private readonly AppExitState _exitState;
    private readonly IAppLogger _logger;
    private readonly TimeProvider _timeProvider;

    [ObservableProperty] private bool _protectionIsOn;
    [ObservableProperty] private bool _protectionWarningAccepted;
    [ObservableProperty] private bool _isBusy;
    [ObservableProperty] private string _statusMessage = string.Empty;
    [ObservableProperty] private bool _statusIsError;

    public string Pin { get; set; } = string.Empty;

    public bool IsNotBusy => !IsBusy;

    public event EventHandler? PasswordBoxesShouldClear;
    public event EventHandler? ExitApproved;
    public event EventHandler? CloseRequested;

    public ExitViewModel(
        IAppLifecycleUseCase lifecycle,
        ProtectionStatusViewModel protection,
        AppExitState exitState,
        IAppLogger logger,
        TimeProvider timeProvider)
    {
        _lifecycle = lifecycle ?? throw new ArgumentNullException(nameof(lifecycle));
        _protection = protection ?? throw new ArgumentNullException(nameof(protection));
        _exitState = exitState ?? throw new ArgumentNullException(nameof(exitState));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _timeProvider = timeProvider ?? throw new ArgumentNullException(nameof(timeProvider));
    }

    partial void OnIsBusyChanged(bool value) => OnPropertyChanged(nameof(IsNotBusy));

    public async Task LoadAsync()
    {
        await _protection.RefreshAsync();
        ProtectionIsOn = _protection.IsEnabled;

        SetStatus(
            ProtectionIsOn
                ? "Bảo vệ đang BẬT. Thoát EduFocus sẽ tắt bảo vệ và kết thúc phiên học hiện tại."
                : "Bảo vệ đang tắt. Nhập PIN phụ huynh để thoát EduFocus.",
            false);
    }

    [RelayCommand]
    private async Task ExitAsync()
    {
        if (IsBusy)
            return;

        if (string.IsNullOrEmpty(Pin))
        {
            SetStatus("Nhập PIN phụ huynh.", true);
            return;
        }

        if (ProtectionIsOn && !ProtectionWarningAccepted)
        {
            SetStatus("Đánh dấu xác nhận tắt bảo vệ trước khi thoát.", true);
            return;
        }

        var pin = Pin;
        Pin = string.Empty;
        PasswordBoxesShouldClear?.Invoke(this, EventArgs.Empty);

        AppExitResult result;
        IsBusy = true;
        SetStatus("Đang xác thực và dọn dẹp bảo vệ...", false);

        try
        {
            result = await Task.Run(() => _lifecycle.ExitWithPinAsync(pin));
        }
        catch (Exception ex)
        {
            _logger.Error(LogCategories.App, "Exit flow failed unexpectedly.", ex);
            SetStatus("Không thể thoát lúc này. EduFocus vẫn đang chạy; vui lòng thử lại.", true);
            return;
        }
        finally
        {
            IsBusy = false;
        }

        if (result.CanExit)
        {
            _exitState.ApproveExit();
            ExitApproved?.Invoke(this, EventArgs.Empty);
            return;
        }

        switch (result.AuthStatus)
        {
            case ParentAuthStatus.NotConfigured:
                SetStatus("Parent Mode chưa được thiết lập nên không thể xác thực PIN.", true);
                break;
            case ParentAuthStatus.LockedOut:
                SetStatus(BuildLockoutMessage(result.LockoutUntilUtc), true);
                break;
            case ParentAuthStatus.InvalidCredential:
                SetStatus($"PIN không đúng. Còn {result.RemainingAttempts} lần thử.", true);
                break;
            default:
                SetStatus(
                    result.Error ?? "Không thể thoát an toàn lúc này. EduFocus vẫn đang chạy.",
                    true);
                break;
        }
    }

    [RelayCommand]
    private void Cancel() => CloseRequested?.Invoke(this, EventArgs.Empty);

    private string BuildLockoutMessage(DateTime? lockoutUntilUtc)
    {
        if (lockoutUntilUtc is not { } until)
            return "Đăng nhập tạm khóa. Vui lòng thử lại sau.";

        var remaining = until - _timeProvider.GetUtcNow().UtcDateTime;
        var minutes = Math.Max(1, (int)Math.Ceiling(remaining.TotalMinutes));
        return $"Đã tạm khóa đăng nhập. Vui lòng thử lại sau {minutes} phút.";
    }

    private void SetStatus(string message, bool isError)
    {
        StatusMessage = message;
        StatusIsError = isError;
    }
}
