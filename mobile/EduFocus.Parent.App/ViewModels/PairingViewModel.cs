using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using EduFocus.Parent.Core;
using EduFocus.Parent.Core.Pairing;
using EduFocus.Parent.Core.Settings;

namespace EduFocus.Parent.App.ViewModels;

/// <summary>
/// Pairing screen logic: QR scan or 8-character code, server URL for self-host,
/// replacement confirmation when already linked, and 429 countdown. The token
/// returned by the claim is stored by <see cref="IPairingService"/> and never
/// touches this view model.
/// </summary>
public sealed partial class PairingViewModel : ObservableObject
{
    private const string CameraDeniedMessage =
        "Ứng dụng chưa có quyền dùng camera. Bạn có thể nhập mã thủ công hoặc cấp quyền trong Cài đặt.";

    private readonly IPairingService _pairing;
    private readonly AppState _appState;
    private readonly ParentSettingsStore _settings;

    private IDispatcherTimer? _retryTimer;
    private DateTimeOffset _retryUntilUtc;
    private Guid? _prefillSessionId;
    private string? _prefillCode;

    [ObservableProperty] private string _cloudBaseUrl;
    [ObservableProperty] private string _codeInput = string.Empty;
    [ObservableProperty] private bool _isBusy;
    [ObservableProperty] private bool _isScannerActive;
    [ObservableProperty] private bool _isAlreadyLinked;
    [ObservableProperty] private bool _cameraPermissionDenied;
    [ObservableProperty] private bool _isRateLimited;
    [ObservableProperty] private string _retryText = string.Empty;
    [ObservableProperty] private string _statusMessage = string.Empty;
    [ObservableProperty] private bool _statusIsError;

    public PairingViewModel(IPairingService pairing, AppState appState, ParentSettingsStore settings)
    {
        _pairing = pairing ?? throw new ArgumentNullException(nameof(pairing));
        _appState = appState ?? throw new ArgumentNullException(nameof(appState));
        _settings = settings ?? throw new ArgumentNullException(nameof(settings));
        _cloudBaseUrl = settings.CloudBaseUrl ?? appState.CloudBaseUrl ?? BuildConfig.CloudRelayBaseUrl;
    }

    public event EventHandler? PairingSucceeded;

    public Func<Task<bool>>? ConfirmReplaceAsync { get; set; }

    public bool IsNotBusy => !IsBusy;

    public bool CanSubmit => !IsBusy && !IsRateLimited && CodeInput.Trim().Length > 0;

    public bool HasStatusMessage => StatusMessage.Length > 0;

    public bool IsInsecureUrl => CloudBaseUrl.TrimStart()
        .StartsWith("http://", StringComparison.OrdinalIgnoreCase);

    public bool ShowStartScanner => !IsScannerActive;

    public bool ShowStopScanner => IsScannerActive;

    public string LinkedBannerText =>
        $"Điện thoại đang ghép nối với {(_appState.DesktopName ?? _settings.DesktopName ?? "một máy tính")}. "
        + "Ghép nối mã mới sẽ thay thế liên kết hiện tại.";

    partial void OnIsBusyChanged(bool value) => NotifyComputed();

    partial void OnIsRateLimitedChanged(bool value) => NotifyComputed();

    partial void OnIsScannerActiveChanged(bool value)
    {
        OnPropertyChanged(nameof(ShowStartScanner));
        OnPropertyChanged(nameof(ShowStopScanner));
    }

    partial void OnCodeInputChanged(string value) => OnPropertyChanged(nameof(CanSubmit));

    partial void OnCloudBaseUrlChanged(string value) => OnPropertyChanged(nameof(IsInsecureUrl));

    partial void OnStatusMessageChanged(string value) => OnPropertyChanged(nameof(HasStatusMessage));

    public void RefreshLinkedState()
    {
        IsAlreadyLinked = _appState.IsLinked;
        if (string.IsNullOrWhiteSpace(CloudBaseUrl))
            CloudBaseUrl = _settings.CloudBaseUrl ?? _appState.CloudBaseUrl ?? BuildConfig.CloudRelayBaseUrl;

        if (_prefillCode is null)
        {
            CodeInput = string.Empty;
            SetStatus(string.Empty, false);
        }

        CameraPermissionDenied = false;
        OnPropertyChanged(nameof(LinkedBannerText));
    }

    public void SetScannerActive(bool active)
    {
        if (active)
            CameraPermissionDenied = false;

        IsScannerActive = active;
    }

    public void ReportCameraPermissionDenied()
    {
        CameraPermissionDenied = true;
        IsScannerActive = false;
        SetStatus(CameraDeniedMessage, true);
    }

    public async Task HandleScannedPayloadAsync(string? raw)
    {
        if (IsBusy || IsRateLimited)
            return;

        if (!PairingPayloadParser.TryParse(raw, AllowInsecure, out var payload, out var error))
        {
            SetStatus(error ?? "Mã QR không hợp lệ.", true);
            return;
        }

        CodeInput = payload!.Code;
        CloudBaseUrl = payload.CloudBaseUrl;
        IsScannerActive = false;
        await ClaimAsync(payload.Code, payload.CloudBaseUrl, payload.SessionId);
    }

    public void PrefillFromLink(string? raw)
    {
        if (!PairingPayloadParser.TryParse(raw, AllowInsecure, out var payload, out var error))
        {
            SetStatus(error ?? "Liên kết ghép nối không hợp lệ.", true);
            return;
        }

        IsScannerActive = false;
        CodeInput = payload!.Code;
        CloudBaseUrl = payload.CloudBaseUrl;
        _prefillSessionId = payload.SessionId;
        _prefillCode = payload.Code;
        SetStatus(
            "Đã nhận liên kết ghép nối. Kiểm tra mã rồi bấm “Ghép nối” để hoàn tất.",
            false);
    }

    [RelayCommand]
    private async Task SubmitAsync()
    {
        if (IsBusy)
            return;

        if (IsRateLimited)
        {
            SetStatus("Bạn đã thử quá nhiều lần. Vui lòng đợi hết thời gian đếm ngược.", true);
            return;
        }

        if (!PairingCodeInput.TryNormalize(CodeInput, out var code))
        {
            SetStatus(PairingCodeInput.Hint, true);
            return;
        }

        if (!CloudServerUrl.TryNormalize(CloudBaseUrl, AllowInsecure, out var url, out var urlError))
        {
            SetStatus(urlError ?? "Địa chỉ cloud không hợp lệ.", true);
            return;
        }

        var sessionId = code == _prefillCode ? _prefillSessionId : null;
        await ClaimAsync(code, url!.GetLeftPart(UriPartial.Authority), sessionId);
    }

    private async Task ClaimAsync(string code, string cloudBaseUrl, Guid? sessionId)
    {
        var replacing = _appState.IsLinked;
        if (replacing && ConfirmReplaceAsync is { } confirm && !await confirm())
            return;

        IsBusy = true;
        SetStatus(replacing ? "Đang thay thế liên kết cũ..." : "Đang ghép nối...", false);

        try
        {
            var response = await _pairing.ClaimAsync(
                new PairingClaim(code, cloudBaseUrl, sessionId),
                Identity(),
                replaceExisting: replacing);

            IsScannerActive = false;
            CameraPermissionDenied = false;
            _prefillSessionId = null;
            _prefillCode = null;
            CodeInput = string.Empty;
            RefreshLinkedState();
            SetStatus($"Ghép nối thành công với {response.DesktopName}.", false);
            PairingSucceeded?.Invoke(this, EventArgs.Empty);
        }
        catch (PairingException ex)
        {
            if (ex.Failure == PairingFailure.RateLimited && ex.RetryAfter is { } retryAfter)
                StartRetryCountdown(retryAfter);

            SetStatus(ex.Message, true);
        }
        catch (Exception)
        {
            SetStatus("Không thể ghép nối lúc này. Vui lòng thử lại.", true);
        }
        finally
        {
            IsBusy = false;
        }
    }

    private void StartRetryCountdown(TimeSpan retryAfter)
    {
        _retryUntilUtc = DateTimeOffset.UtcNow
            + (retryAfter > TimeSpan.Zero ? retryAfter : TimeSpan.FromSeconds(30));
        IsRateLimited = true;

        var dispatcher = Application.Current?.Dispatcher;
        if (dispatcher is null)
        {
            IsRateLimited = false;
            return;
        }

        if (_retryTimer is null)
        {
            _retryTimer = dispatcher.CreateTimer();
            _retryTimer.Interval = TimeSpan.FromSeconds(1);
            _retryTimer.Tick += OnRetryTick;
        }

        if (!_retryTimer.IsRunning)
            _retryTimer.Start();

        UpdateRetryText();
    }

    private void OnRetryTick(object? sender, EventArgs e) => UpdateRetryText();

    private void UpdateRetryText()
    {
        var remaining = _retryUntilUtc - DateTimeOffset.UtcNow;
        if (remaining <= TimeSpan.Zero)
        {
            _retryTimer?.Stop();
            IsRateLimited = false;
            RetryText = string.Empty;
            return;
        }

        RetryText = FormattableString.Invariant(
            $"Có thể thử lại sau {(int)remaining.TotalMinutes:00}:{remaining.Seconds:00}.");
    }

    private void NotifyComputed()
    {
        OnPropertyChanged(nameof(IsNotBusy));
        OnPropertyChanged(nameof(CanSubmit));
    }

    private void SetStatus(string message, bool isError)
    {
        StatusMessage = message;
        StatusIsError = isError;
    }

    private static bool AllowInsecure =>
#if DEBUG
        true;
#else
        false;
#endif

    private static MobileDeviceIdentity Identity()
    {
        var name = DeviceInfo.Current.Name?.Trim();
        if (string.IsNullOrWhiteSpace(name) || name.Length > 128)
            name = "Điện thoại phụ huynh";

        var platform = DeviceInfo.Current.Platform == DevicePlatform.Android ? "android" : "ios";
        return new MobileDeviceIdentity(name, platform, AppInfo.Current.VersionString, TimeZoneInfo.Local.Id);
    }
}
