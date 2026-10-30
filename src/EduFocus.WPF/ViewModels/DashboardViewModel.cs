using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using EduFocus.Domain.Reports;
using EduFocus.Ports.Inbound;
using EduFocus.Ports.Outbound.Services;

namespace EduFocus.WPF.ViewModels;

/// <summary>
/// Today's dashboard for Student Mode (main window) and Parent Mode (UC10,
/// issue #35). One aggregate row is read per refresh and polled on a fixed
/// cadence; activity samples are never scanned. Student Mode binds only the
/// study/session properties, so parent-only counters never surface there.
/// </summary>
public sealed partial class DashboardViewModel : ObservableObject
{
    private readonly IDashboardUseCase _dashboard;
    private readonly IAppLogger _logger;
    private readonly TimeProvider _timeProvider;
    private readonly SemaphoreSlim _gate = new(1, 1);

    private bool _loaded;

    [ObservableProperty] private bool _isLoading = true;
    [ObservableProperty] private bool _hasError;
    [ObservableProperty] private string _errorMessage = string.Empty;
    [ObservableProperty] private bool _hasSessions;
    [ObservableProperty] private string _dateLabel = "Hôm nay";
    [ObservableProperty] private string _studyTimeText = "0 phút";
    [ObservableProperty] private string _activeTimeText = "0 phút";
    [ObservableProperty] private string _distractionTimeText = "0 phút";
    [ObservableProperty] private string _attentionText = "Chưa có dữ liệu";
    [ObservableProperty] private string _coverageText = "Chưa có dữ liệu";
    [ObservableProperty] private string _monitoredTimeText = "0 phút";
    [ObservableProperty] private string _unobservedTimeText = "0 phút";
    [ObservableProperty] private string _cameraUnavailableTimeText = "0 phút";
    [ObservableProperty] private string _blockedAttemptsText = "0";
    [ObservableProperty] private string _sessionCountText = "0 phiên";
    [ObservableProperty] private string _activeSessionText = "Hiện không có phiên học nào đang diễn ra.";
    [ObservableProperty] private string _studentStudyText = "Hôm nay bạn chưa có phiên học nào.";
    [ObservableProperty] private string _coverageWarning = string.Empty;
    [ObservableProperty] private bool _hasCoverageWarning;
    [ObservableProperty] private string _lastUpdatedText = string.Empty;

    public DashboardViewModel(
        IDashboardUseCase dashboard,
        IAppLogger logger,
        TimeProvider timeProvider)
    {
        _dashboard = dashboard ?? throw new ArgumentNullException(nameof(dashboard));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _timeProvider = timeProvider ?? throw new ArgumentNullException(nameof(timeProvider));
    }

    public string EmptyStateText =>
        "Hôm nay chưa có phiên học nào. Khi bảo vệ được bật, thời gian học sẽ xuất hiện ở đây.";

    public bool ShowSummary => !IsLoading && HasSessions;

    public bool ShowEmptyState => !IsLoading && !HasError && !HasSessions;

    partial void OnIsLoadingChanged(bool value) => NotifyVisibility();

    partial void OnHasErrorChanged(bool value) => NotifyVisibility();

    partial void OnHasSessionsChanged(bool value) => NotifyVisibility();

    /// <summary>
    /// Reads the local day again. Overlapping calls are dropped so a slow
    /// database cannot stack polling ticks. Errors keep the last good numbers
    /// and surface a retry banner instead of clearing the dashboard.
    /// </summary>
    public async Task RefreshAsync(CancellationToken cancellationToken = default)
    {
        if (!await _gate.WaitAsync(0, cancellationToken))
            return;

        try
        {
            if (!_loaded)
                IsLoading = true;

            var summary = await _dashboard.GetTodayAsync(cancellationToken);
            Apply(summary);
            _loaded = true;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
        catch (Exception ex)
        {
            _logger.Error(LogCategories.UI, "Could not load the today dashboard.", ex);
            HasError = true;
            ErrorMessage = "Không thể đọc dữ liệu tổng quan hôm nay. Hãy thử lại.";
        }
        finally
        {
            IsLoading = false;
            _gate.Release();
        }
    }

    [RelayCommand]
    private Task RetryAsync() => RefreshAsync();

    private void Apply(DailyStudySummary summary)
    {
        HasError = false;
        ErrorMessage = string.Empty;
        HasSessions = summary.HasSessions;
        DateLabel = $"Hôm nay, {summary.LocalDate:dd/MM/yyyy}";
        StudyTimeText = FormatDuration(summary.StudySeconds);
        ActiveTimeText = FormatDuration(summary.ActiveSeconds);
        DistractionTimeText = FormatDuration(summary.DistractionSeconds);
        MonitoredTimeText = FormatDuration(summary.Attention.MonitoredSeconds);
        UnobservedTimeText = FormatDuration(summary.Attention.UnobservedSeconds);
        CameraUnavailableTimeText = FormatDuration(summary.CameraUnavailableSeconds);
        BlockedAttemptsText = summary.BlockedAttemptCount.ToString(CultureInfo.InvariantCulture);
        SessionCountText = summary.SessionCount == 1 ? "1 phiên" : $"{summary.SessionCount} phiên";
        AttentionText = summary.Attention.AttentionPercent is { } attention
            ? $"{attention:0}%"
            : "Chưa có dữ liệu";
        CoverageText = summary.Attention.CoveragePercent is { } coverage
            ? $"{coverage:0}%"
            : "Chưa có dữ liệu";
        HasCoverageWarning = summary.Attention.IsCoverageLow;
        CoverageWarning = summary.Attention.Warning ?? string.Empty;
        ActiveSessionText = BuildActiveSessionText(summary);
        StudentStudyText = summary.HasSessions
            ? $"Hôm nay bạn đã học {StudyTimeText}."
            : "Hôm nay bạn chưa có phiên học nào.";
        LastUpdatedText = $"Cập nhật lúc {_timeProvider.GetLocalNow():HH:mm:ss}";
    }

    private string BuildActiveSessionText(DailyStudySummary summary)
    {
        if (!summary.HasActiveSession || summary.ActiveSessionStartedAtUtc is not { } startedAtUtc)
            return "Hiện không có phiên học nào đang diễn ra.";

        var localStart = TimeZoneInfo.ConvertTimeFromUtc(startedAtUtc, _timeProvider.LocalTimeZone);
        return $"Phiên học đang diễn ra từ {localStart:HH:mm}.";
    }

    private static string FormatDuration(int seconds)
    {
        var totalMinutes = seconds / 60;

        if (totalMinutes <= 0)
            return seconds > 0 ? "dưới 1 phút" : "0 phút";

        var hours = totalMinutes / 60;
        var minutes = totalMinutes % 60;

        if (hours == 0)
            return $"{minutes} phút";
        if (minutes == 0)
            return $"{hours} giờ";
        return $"{hours} giờ {minutes} phút";
    }

    private void NotifyVisibility()
    {
        OnPropertyChanged(nameof(ShowSummary));
        OnPropertyChanged(nameof(ShowEmptyState));
    }
}
