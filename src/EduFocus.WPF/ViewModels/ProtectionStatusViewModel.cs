using CommunityToolkit.Mvvm.ComponentModel;
using EduFocus.Ports.DTOs;
using EduFocus.Ports.Inbound;
using EduFocus.Ports.Outbound.Services;

namespace EduFocus.WPF.ViewModels;

/// <summary>
/// Shared projection of the persisted protection state for the main window,
/// Parent Mode and the tray icon. The use case owns the state machine; this
/// class only exposes it for binding and keeps every surface in sync.
/// </summary>
public sealed partial class ProtectionStatusViewModel : ObservableObject
{
    private readonly IProtectionUseCase _protection;
    private readonly IAppLogger _logger;
    private readonly TimeProvider _timeProvider;

    [ObservableProperty] private bool _isEnabled;
    [ObservableProperty] private DateTime? _activeSessionStartedAtUtc;
    [ObservableProperty] private bool _monitoringEnabled;
    [ObservableProperty] private string _statusText = "Đang đọc trạng thái bảo vệ...";
    [ObservableProperty] private string _warningText = string.Empty;
    [ObservableProperty] private bool _hasWarning;
    [ObservableProperty] private bool _isBusy;

    public string ToggleButtonText => IsEnabled ? "Tắt bảo vệ" : "Bật bảo vệ";

    public string TrayTooltip => IsEnabled ? "EduFocus — Đang bảo vệ" : "EduFocus — Chưa bảo vệ";

    public bool IsNotBusy => !IsBusy;

    public event EventHandler? TrayStateChanged;

    public ProtectionStatusViewModel(
        IProtectionUseCase protection,
        IAppLogger logger,
        TimeProvider timeProvider)
    {
        _protection = protection ?? throw new ArgumentNullException(nameof(protection));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _timeProvider = timeProvider ?? throw new ArgumentNullException(nameof(timeProvider));
    }

    partial void OnIsEnabledChanged(bool value)
    {
        OnPropertyChanged(nameof(ToggleButtonText));
        OnPropertyChanged(nameof(TrayTooltip));
        TrayStateChanged?.Invoke(this, EventArgs.Empty);
    }

    partial void OnIsBusyChanged(bool value) => OnPropertyChanged(nameof(IsNotBusy));

    public void Apply(ProtectionOperationResult result) => Apply(result.Status, result.Warnings);

    public void Apply(ProtectionStatus status, IReadOnlyList<string> warnings)
    {
        IsEnabled = status.IsEnabled;
        ActiveSessionStartedAtUtc = status.ActiveSessionStartedAtUtc;
        MonitoringEnabled = status.MonitoringEnabled;
        StatusText = BuildStatusText(status);
        SetWarnings(warnings);
    }

    public void ReportFailure(string message)
    {
        StatusText = "Trạng thái bảo vệ chưa được đồng bộ.";
        SetWarnings([message]);
    }

    public async Task RefreshAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            Apply(await _protection.GetStatusAsync(cancellationToken), []);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
        catch (Exception ex)
        {
            _logger.Error(LogCategories.Protection, "Could not read the protection status.", ex);
            ReportFailure("Không thể đọc trạng thái bảo vệ. Hãy mở Parent Mode để kiểm tra.");
        }
    }

    /// <summary>
    /// Enables or disables protection through the state machine. Callers handle
    /// <see cref="EduFocus.Ports.Protection.ProtectionEngineException"/> and
    /// <see cref="EduFocus.Ports.Security.ParentAuthorizationException"/>.
    /// </summary>
    public async Task<ProtectionOperationResult> ToggleAsync(CancellationToken cancellationToken = default)
    {
        IsBusy = true;

        try
        {
            var result = IsEnabled
                ? await _protection.DisableAsync(cancellationToken)
                : await _protection.EnableAsync(cancellationToken);

            Apply(result);
            return result;
        }
        finally
        {
            IsBusy = false;
        }
    }

    private void SetWarnings(IReadOnlyList<string> warnings)
    {
        HasWarning = warnings.Count > 0;
        WarningText = HasWarning ? string.Join(Environment.NewLine, warnings) : string.Empty;
    }

    private string BuildStatusText(ProtectionStatus status)
    {
        if (!status.IsEnabled)
            return "Bảo vệ đang tắt. Trang web và ứng dụng trong danh sách chặn chưa bị giới hạn.";

        if (status.ActiveSessionStartedAtUtc is not { } startedAtUtc)
            return "Bảo vệ đang bật nhưng chưa có phiên học nào đang hoạt động.";

        var localStart = TimeZoneInfo.ConvertTimeFromUtc(startedAtUtc, _timeProvider.LocalTimeZone);
        return $"Bảo vệ đang bật — phiên học bắt đầu lúc {localStart:HH:mm}.";
    }
}
