using System.Globalization;
using System.Windows.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using EduFocus.Ports.DTOs;
using EduFocus.Ports.Inbound;
using EduFocus.Ports.Outbound.Cloud;
using EduFocus.Ports.Outbound.Services;
using EduFocus.Ports.Security;
using EduFocus.UseCases.Cloud;
using EduFocus.WPF.Services;

namespace EduFocus.WPF.ViewModels;

/// <summary>
/// "Kết nối điện thoại" card inside Parent Mode (UC15, UC18, issue #89). Only
/// talks to <see cref="ICloudLinkUseCase"/>; the desktop token never reaches
/// this view model, the QR payload and pairing code are never logged, and
/// pairing is polled every 5 seconds only while the card is actually open.
/// </summary>
public sealed partial class CloudLinkViewModel : ObservableObject
{
    public const string DeleteConfirmationWord = "XÓA";

    public static readonly TimeSpan PairingPollInterval = TimeSpan.FromSeconds(5);

    private const string SessionExpiredMessage =
        "Phiên Parent Mode đã hết hạn. Vui lòng đăng nhập lại.";

    private static readonly string DesktopKind = "Mobile";

    private readonly ICloudLinkUseCase _links;
    private readonly ICloudOutboxNotifier _notifier;
    private readonly IClipboardService _clipboard;
    private readonly IAppLogger _logger;
    private readonly TimeProvider _timeProvider;
    private readonly ICloudOutboxUseCase? _outbox;

    private DateTimeOffset? _lastPollAtUtc;
    private DateTime? _pairingExpiresAtUtc;
    private Guid? _qrSessionId;
    private long _operationGeneration;
    private bool _windowOpen;
    private bool _sectionVisible;
    private bool _refreshing;

    [ObservableProperty] private bool _isBusy;
    [ObservableProperty] private bool _consentAccepted;
    [ObservableProperty] private string _cloudBaseUrl = CloudRelayDefaults.GetBaseUrl();
    [ObservableProperty] private string _desktopName = CloudRelayDefaults.GetDesktopName();
    [ObservableProperty] private string _statusMessage = string.Empty;
    [ObservableProperty] private bool _statusIsError;

    [ObservableProperty] private bool _isCloudEnabled;
    [ObservableProperty] private bool _isLinked;
    [ObservableProperty] private bool _hasActivePairing;
    [ObservableProperty] private bool _isPairingExpired;
    [ObservableProperty] private string _pairingCode = string.Empty;
    [ObservableProperty] private string _pairingCountdown = string.Empty;
    [ObservableProperty] private ImageSource? _qrImage;

    [ObservableProperty] private string _linkedDeviceText = "Chưa có điện thoại nào được ghép nối.";
    [ObservableProperty] private string _linkedSinceText = "Chưa ghép nối.";
    [ObservableProperty] private string _lastSyncText = "Chưa đồng bộ lần nào.";
    [ObservableProperty] private string _pendingEventsText = "Không có sự kiện chờ gửi.";
    [ObservableProperty] private string _lastErrorText = string.Empty;

    [ObservableProperty] private bool _showUnlinkConfirm;
    [ObservableProperty] private bool _deleteCloudData;
    [ObservableProperty] private string _deleteConfirmation = string.Empty;

    public CloudLinkViewModel(
        ICloudLinkUseCase links,
        ICloudOutboxNotifier notifier,
        IClipboardService clipboard,
        IAppLogger logger,
        TimeProvider timeProvider,
        ICloudOutboxUseCase? outbox = null)
    {
        _links = links ?? throw new ArgumentNullException(nameof(links));
        _notifier = notifier ?? throw new ArgumentNullException(nameof(notifier));
        _clipboard = clipboard ?? throw new ArgumentNullException(nameof(clipboard));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _timeProvider = timeProvider ?? throw new ArgumentNullException(nameof(timeProvider));
        _outbox = outbox;
    }

    public event EventHandler? PrivacyNoticeRequested;

    public bool IsNotBusy => !IsBusy;

    public bool CanEnable => ConsentAccepted && !IsBusy;

    public bool CanCreateNewPairing =>
        IsCloudEnabled && !IsLinked && !HasActivePairing && !IsBusy;

    public bool ShowEnablePanel => !IsCloudEnabled;

    public bool ShowPairingPanel => IsCloudEnabled && !IsLinked;

    public bool ShowLinkedPanel => IsLinked;

    public bool HasPairingMaterial => !string.IsNullOrEmpty(PairingCode) || QrImage is not null;

    public bool ShowPairingMaterial => HasPairingMaterial;

    public bool ShowPairingPrompt => IsCloudEnabled && !IsLinked && !HasPairingMaterial;

    public bool HasStatusMessage => StatusMessage.Length > 0;

    public bool HasLastError => LastErrorText.Length > 0;

    public bool IsInsecureUrl =>
        CloudBaseUrl.TrimStart().StartsWith("http://", StringComparison.OrdinalIgnoreCase);

    public string ConnectionStateText =>
        !IsCloudEnabled ? "Chưa bật"
        : IsLinked ? "Đã ghép nối"
        : HasActivePairing ? "Chờ ghép nối"
        : "Chờ tạo mã";

    private static bool AllowInsecureDevelopmentEndpoints =>
#if DEBUG
        true;
#else
        false;
#endif

    partial void OnIsBusyChanged(bool value) => NotifyStateChanged();

    partial void OnConsentAcceptedChanged(bool value) => NotifyStateChanged();

    partial void OnIsCloudEnabledChanged(bool value) => NotifyStateChanged();

    partial void OnIsLinkedChanged(bool value) => NotifyStateChanged();

    partial void OnHasActivePairingChanged(bool value) => NotifyStateChanged();

    partial void OnIsPairingExpiredChanged(bool value) => NotifyStateChanged();

    partial void OnPairingCodeChanged(string value) => NotifyStateChanged();

    partial void OnQrImageChanged(ImageSource? value) => NotifyStateChanged();

    partial void OnStatusMessageChanged(string value) => OnPropertyChanged(nameof(HasStatusMessage));

    partial void OnLastErrorTextChanged(string value) => OnPropertyChanged(nameof(HasLastError));

    partial void OnCloudBaseUrlChanged(string value) => OnPropertyChanged(nameof(IsInsecureUrl));

    public void HandleWindowOpened()
    {
        _windowOpen = true;
        _lastPollAtUtc = null;
    }

    public void HandleWindowClosed()
    {
        _operationGeneration++;
        _windowOpen = false;
        _sectionVisible = false;
        _lastPollAtUtc = null;

        // The pairing code is short lived, but nothing sensitive should stay in
        // memory once the card is gone; it is restored by the next snapshot.
        ClearPairing();
        ShowUnlinkConfirm = false;
        DeleteCloudData = false;
        DeleteConfirmation = string.Empty;
        SetStatus(string.Empty, false);
    }

    public void SetSectionVisible(bool visible)
    {
        if (_sectionVisible == visible)
            return;

        _sectionVisible = visible;
        if (visible)
            _lastPollAtUtc = null;
    }

    /// <summary>Called by the window timer; polls only while the card is open and pending.</summary>
    public void HandleTick()
    {
        if (!_windowOpen || !_sectionVisible)
            return;

        if (_pairingExpiresAtUtc is not null)
            UpdatePairingState();

        if (!IsCloudEnabled || IsLinked || _refreshing || IsBusy)
            return;

        var now = _timeProvider.GetUtcNow();
        if (_lastPollAtUtc is { } last && now - last < PairingPollInterval)
            return;

        _lastPollAtUtc = now;
        _ = PollAsync();
    }

    public Task RefreshAsync() => RefreshCoreAsync(showBusy: false);

    [RelayCommand]
    private async Task RefreshStatusAsync() => await RefreshCoreAsync(showBusy: true);

    [RelayCommand]
    private async Task EnableAsync() => await StartPairingAsync();

    [RelayCommand]
    private async Task CreateNewPairingAsync() => await StartPairingAsync();

    [RelayCommand]
    private void SyncNow()
    {
        _notifier.NotifyNewEvent();
        SetStatus("Đã yêu cầu đồng bộ ngay. Sự kiện sẽ được gửi khi có mạng.", false);
    }

    [RelayCommand]
    private void CopyPairingCode()
    {
        if (string.IsNullOrEmpty(PairingCode))
            return;

        var copied = _clipboard.TrySetText(PairingCode);
        SetStatus(
            copied
                ? "Đã sao chép mã ghép nối."
                : "Không thể sao chép tự động. Hãy chọn mã và sao chép thủ công.",
            !copied);
    }

    [RelayCommand]
    private void OpenUnlinkConfirm()
    {
        DeleteConfirmation = string.Empty;
        DeleteCloudData = false;
        ShowUnlinkConfirm = true;
    }

    [RelayCommand]
    private void CancelUnlink()
    {
        ShowUnlinkConfirm = false;
        DeleteCloudData = false;
        DeleteConfirmation = string.Empty;
    }

    [RelayCommand]
    private void OpenPrivacyNotice() => PrivacyNoticeRequested?.Invoke(this, EventArgs.Empty);

    [RelayCommand]
    private async Task UnlinkAsync()
    {
        if (IsBusy)
            return;

        var deleteCloudData = DeleteCloudData;
        if (deleteCloudData
            && !string.Equals(
                DeleteConfirmation?.Trim(),
                DeleteConfirmationWord,
                StringComparison.Ordinal))
        {
            SetStatus($"Gõ đúng “{DeleteConfirmationWord}” để xác nhận xóa dữ liệu cloud.", true);
            return;
        }

        var generation = ++_operationGeneration;
        IsBusy = true;
        SetStatus(
            deleteCloudData
                ? "Đang ngắt kết nối và xóa dữ liệu cloud..."
                : "Đang ngắt kết nối cloud...",
            false);

        try
        {
            await _links.UnlinkAsync(deleteCloudData).ConfigureAwait(true);

            ResetAfterUnlink();
            SetStatus(
                deleteCloudData
                    ? "Đã ngắt kết nối và xóa dữ liệu cloud. Dữ liệu học tập trên máy này không bị ảnh hưởng."
                    : "Đã ngắt kết nối cloud. Dữ liệu học tập trên máy này không bị ảnh hưởng.",
                false);

            await RefreshCoreAsync(showBusy: false, expectedGeneration: generation).ConfigureAwait(true);
        }
        catch (ParentAuthorizationException)
        {
            ShowUnlinkConfirm = false;
            SetStatus(SessionExpiredMessage, true);
        }
        catch (CloudSyncException ex)
        {
            // The link stays intact so the parent can retry the remote revoke.
            _logger.Warning(
                LogCategories.Cloud,
                $"Cloud unlink failed ('{ex.Code}'); the link was kept for retry.");
            SetStatus(ex.Message, true);
            await RefreshCoreAsync(showBusy: false, expectedGeneration: generation).ConfigureAwait(true);
        }
        catch (Exception ex)
        {
            _logger.Error(LogCategories.Cloud, "Cloud unlink failed unexpectedly.", ex);
            SetStatus("Không thể ngắt kết nối cloud lúc này. Vui lòng thử lại.", true);
        }
        finally
        {
            IsBusy = false;
        }
    }

    private async Task StartPairingAsync()
    {
        if (IsBusy)
            return;

        if (!ConsentAccepted)
        {
            SetStatus(
                "Tick đồng ý gửi dữ liệu tổng hợp qua cloud trước khi bật kết nối.",
                true);
            return;
        }

        if (!CloudUrlPolicy.TryNormalize(
                CloudBaseUrl,
                AllowInsecureDevelopmentEndpoints,
                out var baseUrl,
                out var urlError))
        {
            SetStatus(urlError ?? "Địa chỉ cloud không hợp lệ.", true);
            return;
        }

        var desktopName = DesktopName?.Trim() ?? string.Empty;
        if (desktopName.Length == 0)
        {
            SetStatus("Nhập tên cho máy này để điện thoại nhận ra khi ghép nối.", true);
            return;
        }

        if (desktopName.Length > 128)
        {
            SetStatus("Tên máy tối đa 128 ký tự.", true);
            return;
        }

        var generation = ++_operationGeneration;
        IsBusy = true;
        SetStatus("Đang tạo mã ghép nối...", false);

        try
        {
            var snapshot = await _links.GetStatusAsync().ConfigureAwait(true);
            if (generation != _operationGeneration)
                return;

            if (!snapshot.HasConsent)
                await _links.AcceptConsentAsync().ConfigureAwait(true);
            if (generation != _operationGeneration)
                return;

            var pairing = await _links
                .EnableAsync(new CloudLinkOptions(baseUrl!.ToString().TrimEnd('/'), desktopName))
                .ConfigureAwait(true);
            if (generation != _operationGeneration)
                return;

            IsCloudEnabled = true;
            IsLinked = false;
            ConsentAccepted = true;
            CloudBaseUrl = pairing.CloudBaseUrl;
            ApplyPairing(pairing);
            _lastPollAtUtc = _timeProvider.GetUtcNow();

            SetStatus(
                "Đã tạo mã ghép nối. Mở ứng dụng “EduFocus Phụ huynh” trên điện thoại để hoàn tất.",
                false);
        }
        catch (ParentAuthorizationException)
        {
            SetStatus(SessionExpiredMessage, true);
        }
        catch (ArgumentException ex)
        {
            SetStatus(ex.Message, true);
        }
        catch (InvalidOperationException ex)
        {
            SetStatus(ex.Message, true);
        }
        catch (CloudSyncException ex)
        {
            _logger.Warning(
                LogCategories.Cloud,
                $"Creating the cloud pairing session failed with code '{ex.Code}'.");
            SetStatus(ex.Message, true);
        }
        catch (Exception ex)
        {
            _logger.Error(LogCategories.Cloud, "Creating the cloud pairing session failed unexpectedly.", ex);
            SetStatus(
                "Không thể bật kết nối cloud lúc này. Kiểm tra kết nối mạng rồi thử lại.",
                true);
        }
        finally
        {
            IsBusy = false;
        }
    }

    private async Task RefreshCoreAsync(bool showBusy, long? expectedGeneration = null)
    {
        if (showBusy)
        {
            if (IsBusy)
                return;

            IsBusy = true;
        }

        var generation = expectedGeneration ?? _operationGeneration;
        try
        {
            var snapshot = await _links.GetStatusAsync().ConfigureAwait(true);
            var outboxStatus = _outbox is not null
                ? await _outbox.GetStatusAsync().ConfigureAwait(true)
                : null;
            if (generation != _operationGeneration)
                return;

            ApplySnapshot(snapshot, outboxStatus);
        }
        catch (Exception ex)
        {
            if (generation != _operationGeneration)
                return;

            if (showBusy)
            {
                _logger.Error(LogCategories.Cloud, "Could not read the cloud link status.", ex);
                SetStatus("Không thể đọc trạng thái kết nối cloud lúc này. Vui lòng thử lại.", true);
            }
            else
            {
                _logger.Warning(
                    LogCategories.Cloud,
                    $"Could not refresh the cloud link status ({ex.GetType().Name}).");
            }
        }
        finally
        {
            if (showBusy)
                IsBusy = false;
        }
    }

    private async Task PollAsync()
    {
        if (_refreshing || IsBusy)
            return;

        var generation = _operationGeneration;
        _refreshing = true;
        try
        {
            await RefreshCoreAsync(showBusy: false, expectedGeneration: generation).ConfigureAwait(true);
        }
        finally
        {
            _refreshing = false;
        }
    }

    private void ApplySnapshot(CloudLinkStatusSnapshot snapshot, CloudOutboxStatus? outboxStatus = null)
    {
        IsCloudEnabled = snapshot.IsEnabled;
        IsLinked = snapshot.IsLinked;

        if (snapshot.HasConsent)
            ConsentAccepted = true;

        if (!string.IsNullOrWhiteSpace(snapshot.CloudBaseUrl))
            CloudBaseUrl = snapshot.CloudBaseUrl!;

        if (snapshot.ActivePairing is { } pairing)
        {
            ApplyPairing(pairing);
        }
        else if (snapshot.IsLinked || !snapshot.IsEnabled)
        {
            ClearPairing();
        }

        LinkedDeviceText = BuildDeviceText(snapshot);
        LinkedSinceText = FormatLocal(snapshot.LinkedAtUtc, "Chưa ghép nối.");
        LastSyncText = FormatLocal(snapshot.LastSyncAtUtc, "Chưa đồng bộ lần nào.");
        PendingEventsText = BuildPendingEventsText(snapshot, outboxStatus);
        LastErrorText = snapshot.LastError ?? string.Empty;

        UpdatePairingState();
    }

    private static string BuildPendingEventsText(
        CloudLinkStatusSnapshot snapshot,
        CloudOutboxStatus? outboxStatus)
    {
        var localPending = (outboxStatus?.PendingCount ?? 0) + (outboxStatus?.FailedCount ?? 0);

        if (!snapshot.RemoteStatusChecked)
        {
            if (localPending > 0)
            {
                return FormattableString.Invariant(
                    $"Trên máy: {localPending} sự kiện chờ gửi · Máy chủ: không kiểm tra được.");
            }

            return snapshot.IsLinked
                ? "Không kiểm tra được máy chủ."
                : "Không có sự kiện chờ gửi.";
        }

        if (localPending == 0 && snapshot.PendingSyncEvents == 0)
            return "Không có sự kiện chờ gửi.";

        return FormattableString.Invariant(
            $"Trên máy: {localPending} chờ gửi · Máy chủ: {snapshot.PendingSyncEvents} đang xử lý.");
    }

    private void ApplyPairing(CloudPairingInfo pairing)
    {
        if (_qrSessionId != pairing.SessionId)
        {
            _qrSessionId = pairing.SessionId;
            QrImage = CloudQrCode.TryRender(pairing.QrPayload);
        }

        PairingCode = pairing.Code;
        _pairingExpiresAtUtc = pairing.ExpiresAtUtc;
        UpdatePairingState();
    }

    private void ClearPairing()
    {
        _qrSessionId = null;
        _pairingExpiresAtUtc = null;
        PairingCode = string.Empty;
        QrImage = null;
        UpdatePairingState();
    }

    private void UpdatePairingState()
    {
        var now = _timeProvider.GetUtcNow().UtcDateTime;
        var remaining = _pairingExpiresAtUtc is { } expires ? expires - now : (TimeSpan?)null;
        var active = remaining is { } value && value > TimeSpan.Zero;

        HasActivePairing = active;
        IsPairingExpired = HasPairingMaterial && IsCloudEnabled && !IsLinked && !active;
        PairingCountdown = remaining switch
        {
            null => string.Empty,
            { } left when left > TimeSpan.Zero =>
                $"Mã hết hạn sau {FormatRemaining(left)}.",
            _ => "Mã ghép nối đã hết hạn."
        };
    }

    private void ResetAfterUnlink()
    {
        ShowUnlinkConfirm = false;
        DeleteCloudData = false;
        DeleteConfirmation = string.Empty;
        ClearPairing();
        IsLinked = false;
        IsCloudEnabled = false;
        LinkedDeviceText = "Chưa có điện thoại nào được ghép nối.";
        LinkedSinceText = "Chưa ghép nối.";
        LastSyncText = "Chưa đồng bộ lần nào.";
        PendingEventsText = "Không có sự kiện chờ gửi.";
        LastErrorText = string.Empty;
    }

    private void NotifyStateChanged()
    {
        OnPropertyChanged(nameof(IsNotBusy));
        OnPropertyChanged(nameof(CanEnable));
        OnPropertyChanged(nameof(CanCreateNewPairing));
        OnPropertyChanged(nameof(ShowEnablePanel));
        OnPropertyChanged(nameof(ShowPairingPanel));
        OnPropertyChanged(nameof(ShowLinkedPanel));
        OnPropertyChanged(nameof(HasPairingMaterial));
        OnPropertyChanged(nameof(ShowPairingMaterial));
        OnPropertyChanged(nameof(ShowPairingPrompt));
        OnPropertyChanged(nameof(ConnectionStateText));
    }

    private static string BuildDeviceText(CloudLinkStatusSnapshot snapshot)
    {
        var phones = snapshot.Devices
            .Where(device => !device.IsRevoked
                && device.Kind.Equals(DesktopKind, StringComparison.OrdinalIgnoreCase))
            .ToList();

        if (phones.Count > 0)
        {
            return string.Join(
                ", ",
                phones.Select(device => $"{device.Name} ({PlatformLabel(device.Platform)})"));
        }

        return snapshot.RemoteStatusChecked
            ? "Chưa có điện thoại nào được ghép nối."
            : "Chưa đọc được danh sách thiết bị từ máy chủ cloud.";
    }

    private static string PlatformLabel(string platform) =>
        platform.Trim().ToLowerInvariant() switch
        {
            "android" => "Android",
            "ios" => "iOS",
            "windows" => "Windows",
            "" => "không rõ nền tảng",
            _ => platform.Trim()
        };

    private static string FormatRemaining(TimeSpan remaining) =>
        FormattableString.Invariant(
            $"{(int)remaining.TotalMinutes:00}:{remaining.Seconds:00}");

    private static string FormatLocal(DateTime? utc, string empty)
    {
        if (utc is not { } value)
            return empty;

        var local = DateTime.SpecifyKind(value, DateTimeKind.Utc).ToLocalTime();
        return local.ToString("HH:mm dd/MM/yyyy", CultureInfo.InvariantCulture);
    }

    private void SetStatus(string message, bool isError)
    {
        StatusMessage = message;
        StatusIsError = isError;
    }
}
