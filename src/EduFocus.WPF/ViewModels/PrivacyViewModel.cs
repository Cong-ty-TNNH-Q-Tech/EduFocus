using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using EduFocus.Domain.Enums;
using EduFocus.Ports.DTOs;
using EduFocus.Ports.Inbound;
using EduFocus.Ports.Security;
using EduFocus.Ports.Outbound.Services;

namespace EduFocus.WPF.ViewModels;

/// <summary>
/// Data and privacy card inside Parent Mode (UC11, issue #38). Secrets are only
/// passed through to the use case, cleared from the view afterwards and never
/// logged. Deletion needs a typed confirmation plus a fresh PIN verification.
/// </summary>
public sealed partial class PrivacyViewModel : ObservableObject
{
    public const string DeleteConfirmationWord = "XÓA";

    private const string SessionExpiredMessage =
        "Phiên Parent Mode đã hết hạn. Vui lòng đăng nhập lại.";

    private readonly IPrivacySettingsUseCase _settings;
    private readonly IDataDeletionUseCase _deletion;
    private readonly IActivityUseCase _activity;
    private readonly IAppLogger _logger;

    private string? _cameraDeviceId;

    [ObservableProperty] private bool _isBusy;
    [ObservableProperty] private bool _isDeleting;

    [ObservableProperty] private bool _monitoringEnabled;
    [ObservableProperty] private bool _monitorOnlyDuringClasses;
    [ObservableProperty] private string _distractionThreshold = "20";
    [ObservableProperty] private string _absenceThreshold = "30";
    [ObservableProperty] private string _sampleInterval = "15";
    [ObservableProperty] private string _activityRetentionDays = "30";
    [ObservableProperty] private string _eventRetentionDays = "90";
    [ObservableProperty] private string _sessionRetentionDays = "365";
    [ObservableProperty] private bool _startWithWindows;
    [ObservableProperty] private bool _updateCheckEnabled;
    [ObservableProperty] private int _themeIndex;
    [ObservableProperty] private int _languageIndex;

    [ObservableProperty] private string _databaseSizeText = "—";
    [ObservableProperty] private string _storageSummary = string.Empty;
    [ObservableProperty] private string _retentionSummary = string.Empty;
    [ObservableProperty] private string _cameraSummary = string.Empty;

    [ObservableProperty] private string _settingsMessage = string.Empty;
    [ObservableProperty] private bool _settingsIsError;
    [ObservableProperty] private string _deleteMessage = string.Empty;
    [ObservableProperty] private bool _deleteIsError;

    public string DeletePin { get; set; } = string.Empty;
    public string DeleteConfirmation { get; set; } = string.Empty;

    public PrivacyViewModel(
        IPrivacySettingsUseCase settings,
        IDataDeletionUseCase deletion,
        IActivityUseCase activity,
        IAppLogger logger)
    {
        _settings = settings ?? throw new ArgumentNullException(nameof(settings));
        _deletion = deletion ?? throw new ArgumentNullException(nameof(deletion));
        _activity = activity ?? throw new ArgumentNullException(nameof(activity));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public bool IsNotBusy => !IsBusy;

    public bool HasSettingsMessage => SettingsMessage.Length > 0;

    public bool HasDeleteMessage => DeleteMessage.Length > 0;

    public event EventHandler? DeleteSecretShouldClear;

    partial void OnIsBusyChanged(bool value) => OnPropertyChanged(nameof(IsNotBusy));

    partial void OnSettingsMessageChanged(string value) => OnPropertyChanged(nameof(HasSettingsMessage));

    partial void OnDeleteMessageChanged(string value) => OnPropertyChanged(nameof(HasDeleteMessage));

    [RelayCommand]
    public async Task RefreshAsync()
    {
        if (IsBusy)
            return;

        IsBusy = true;
        try
        {
            await LoadSnapshotAsync();
            SetSettingsMessage(string.Empty, false);
            SetDeleteMessage(string.Empty, false);
        }
        catch (ParentAuthorizationException)
        {
            SetSettingsMessage(SessionExpiredMessage, true);
        }
        catch (Exception ex)
        {
            _logger.Error(LogCategories.Database, "Could not load the privacy snapshot.", ex);
            SetSettingsMessage("Không thể tải dữ liệu quyền riêng tư lúc này.", true);
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    private async Task SaveAsync()
    {
        if (IsBusy)
            return;

        if (!TryBuildSettings(out var update, out var validationMessage))
        {
            SetSettingsMessage(validationMessage, true);
            return;
        }

        IsBusy = true;
        try
        {
            var result = await _settings.SaveAsync(update);

            try
            {
                await _activity.RefreshSettingsAsync();
            }
            catch (Exception ex)
            {
                _logger.Warning(
                    LogCategories.Session,
                    $"Could not apply monitoring settings to the running session ({ex.GetType().Name}).");
            }

            ApplySettings(result.Settings);
            SetSettingsMessage(
                result.Changed
                    ? "Đã lưu. Giới hạn giám sát và sample áp dụng ngay; retention áp dụng ở lần dọn kế tiếp; lựa chọn theme/ngôn ngữ được lưu cho bản có hỗ trợ."
                    : "Không có thay đổi để lưu.",
                false);
        }
        catch (ParentAuthorizationException)
        {
            SetSettingsMessage(SessionExpiredMessage, true);
        }
        catch (ArgumentException)
        {
            SetSettingsMessage(
                "Giá trị không hợp lệ. Ngưỡng và sample trong 1–3600 giây; retention trong 1–3650 ngày.",
                true);
        }
        catch (Exception ex)
        {
            _logger.Error(LogCategories.Database, "Could not save privacy settings.", ex);
            SetSettingsMessage("Không thể lưu cài đặt lúc này. Vui lòng thử lại.", true);
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    private async Task DeleteAllAsync()
    {
        if (IsBusy)
            return;

        if (!string.Equals(
                DeleteConfirmation?.Trim(),
                DeleteConfirmationWord,
                StringComparison.Ordinal))
        {
            SetDeleteMessage($"Gõ đúng “{DeleteConfirmationWord}” để xác nhận xóa.", true);
            return;
        }

        if (string.IsNullOrEmpty(DeletePin))
        {
            SetDeleteMessage("Nhập PIN phụ huynh để xác nhận xóa.", true);
            return;
        }

        IsBusy = true;
        IsDeleting = true;
        SetDeleteMessage("Đang xóa dữ liệu...", false);

        try
        {
            var result = await _deletion.DeleteAllAsync(
                new DataDeletionRequest(DeletePin, DeleteConfirmation ?? string.Empty));

            switch (result.Status)
            {
                case DataDeletionStatus.Success:
                    SetDeleteMessage(
                        BuildSuccessMessage(result.Deleted),
                        false);
                    break;
                case DataDeletionStatus.Incomplete:
                    SetDeleteMessage(
                        $"{BuildSuccessMessage(result.Deleted)} Chưa hoàn tất: {string.Join(", ", result.Failures)}. Không có dữ liệu nào bị báo là đã xóa sai.",
                        true);
                    break;
                case DataDeletionStatus.ConfirmationMismatch:
                    SetDeleteMessage($"Gõ đúng “{DeleteConfirmationWord}” để xác nhận xóa.", true);
                    break;
                case DataDeletionStatus.InvalidPin:
                    SetDeleteMessage(
                        $"PIN không đúng. Còn {result.RemainingAttempts} lần thử.",
                        true);
                    break;
                case DataDeletionStatus.LockedOut:
                    SetDeleteMessage("Đăng nhập đang tạm khóa. Vui lòng thử lại sau.", true);
                    break;
                case DataDeletionStatus.NotConfigured:
                    SetDeleteMessage("Parent Mode chưa được thiết lập trên máy này.", true);
                    break;
            }

            await LoadSnapshotAsync();
        }
        catch (ParentAuthorizationException)
        {
            SetDeleteMessage(SessionExpiredMessage, true);
        }
        catch (OperationCanceledException)
        {
            SetDeleteMessage(
                "Đã hủy xóa. Một phần dữ liệu có thể đã bị xóa; hãy kiểm tra dung lượng bên dưới.",
                true);

            try
            {
                await LoadSnapshotAsync();
            }
            catch (Exception ex)
            {
                _logger.Warning(LogCategories.Database, $"Could not refresh after cancel ({ex.GetType().Name}).");
            }
        }
        catch (Exception ex)
        {
            _logger.Error(LogCategories.Database, "Delete-all failed unexpectedly.", ex);
            SetDeleteMessage(
                "Không thể xóa dữ liệu lúc này. Một phần dữ liệu có thể đã bị xóa; hãy thử lại và kiểm tra dung lượng.",
                true);
        }
        finally
        {
            IsDeleting = false;
            IsBusy = false;
            ClearDeleteSecrets();
        }
    }

    private async Task LoadSnapshotAsync()
    {
        var snapshot = await _settings.GetSnapshotAsync();
        ApplySettings(snapshot.Settings);

        DatabaseSizeText = FormatBytes(snapshot.DatabaseSizeBytes);
        StorageSummary = FormattableString.Invariant(
            $"{snapshot.Counts.Sessions} phiên ({snapshot.Counts.ActiveSessions} đang hoạt động), {snapshot.Counts.ActivitySamples} mẫu hoạt động, {snapshot.Counts.AttentionEvents} sự kiện tập trung, {snapshot.Counts.BlockedAttempts} lần chặn.");
        RetentionSummary = FormattableString.Invariant(
            $"Hoạt động {snapshot.Settings.ActivityRetentionDays} ngày • Sự kiện {snapshot.Settings.EventRetentionDays} ngày • Phiên học {snapshot.Settings.SessionRetentionDays} ngày.");
        CameraSummary = snapshot.Settings.CameraDeviceId is null
            ? "Camera mặc định của Windows (chọn thiết bị ở thẻ Camera)."
            : "Đang dùng thiết bị camera đã chọn trong thẻ Camera.";
    }

    private void ApplySettings(PrivacySettings settings)
    {
        MonitoringEnabled = settings.MonitoringEnabled;
        MonitorOnlyDuringClasses = settings.MonitorOnlyDuringClasses;
        DistractionThreshold = settings.DistractionThresholdSeconds.ToString(CultureInfo.InvariantCulture);
        AbsenceThreshold = settings.AbsenceThresholdSeconds.ToString(CultureInfo.InvariantCulture);
        SampleInterval = settings.SampleIntervalSeconds.ToString(CultureInfo.InvariantCulture);
        ActivityRetentionDays = settings.ActivityRetentionDays.ToString(CultureInfo.InvariantCulture);
        EventRetentionDays = settings.EventRetentionDays.ToString(CultureInfo.InvariantCulture);
        SessionRetentionDays = settings.SessionRetentionDays.ToString(CultureInfo.InvariantCulture);
        StartWithWindows = settings.StartWithWindows;
        UpdateCheckEnabled = settings.UpdateCheckEnabled;
        ThemeIndex = settings.Theme switch
        {
            AppTheme.Dark => 0,
            AppTheme.Light => 1,
            _ => 2
        };
        LanguageIndex = settings.Language == AppLanguage.En ? 1 : 0;
        _cameraDeviceId = settings.CameraDeviceId;
    }

    private bool TryBuildSettings(out PrivacySettings settings, out string message)
    {
        settings = null!;

        if (!TryParse(DistractionThreshold, 1, 3600, "Ngưỡng xao nhãng", out var distraction, out message) ||
            !TryParse(AbsenceThreshold, 1, 3600, "Ngưỡng vắng mặt", out var absence, out message) ||
            !TryParse(SampleInterval, 1, 3600, "Khoảng ghi mẫu", out var sample, out message) ||
            !TryParse(ActivityRetentionDays, 1, 3650, "Retention hoạt động", out var activityDays, out message) ||
            !TryParse(EventRetentionDays, 1, 3650, "Retention sự kiện", out var eventDays, out message) ||
            !TryParse(SessionRetentionDays, 1, 3650, "Retention phiên học", out var sessionDays, out message))
        {
            return false;
        }

        settings = new PrivacySettings(
            MonitoringEnabled,
            MonitorOnlyDuringClasses,
            distraction,
            absence,
            sample,
            _cameraDeviceId,
            activityDays,
            eventDays,
            sessionDays,
            StartWithWindows,
            UpdateCheckEnabled,
            ThemeIndex switch
            {
                1 => AppTheme.Light,
                2 => AppTheme.System,
                _ => AppTheme.Dark
            },
            LanguageIndex == 1 ? AppLanguage.En : AppLanguage.Vi);

        return true;
    }

    private static bool TryParse(
        string? text,
        int min,
        int max,
        string label,
        out int value,
        out string message)
    {
        if (!int.TryParse(text?.Trim(), NumberStyles.None, CultureInfo.InvariantCulture, out value) ||
            value < min ||
            value > max)
        {
            message = $"{label} phải là số trong khoảng {min}–{max}.";
            value = 0;
            return false;
        }

        message = string.Empty;
        return true;
    }

    private static string BuildSuccessMessage(DataDeletionCounts deleted) =>
        FormattableString.Invariant(
            $"Đã xóa vĩnh viễn {deleted.Sessions} phiên, {deleted.ActivitySamples} mẫu hoạt động, {deleted.AttentionEvents} sự kiện tập trung và {deleted.BlockedAttempts} lần chặn. Phiên đang hoạt động, cài đặt, PIN và audit được giữ lại.");

    private static string FormatBytes(long bytes)
    {
        const double Kilobyte = 1024;
        const double Megabyte = Kilobyte * 1024;

        return bytes switch
        {
            >= (long)Megabyte => $"{bytes / Megabyte:N1} MB",
            >= (long)Kilobyte => $"{bytes / Kilobyte:N1} KB",
            _ => $"{bytes} B"
        };
    }

    private void ClearDeleteSecrets()
    {
        DeletePin = string.Empty;
        DeleteConfirmation = string.Empty;
        DeleteSecretShouldClear?.Invoke(this, EventArgs.Empty);
    }

    private void SetSettingsMessage(string message, bool isError)
    {
        SettingsMessage = message;
        SettingsIsError = isError;
    }

    private void SetDeleteMessage(string message, bool isError)
    {
        DeleteMessage = message;
        DeleteIsError = isError;
    }
}
