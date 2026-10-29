using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using EduFocus.Domain.Attention;
using EduFocus.Domain.Entities;
using EduFocus.Domain.Enums;
using EduFocus.Domain.Reports;
using EduFocus.Ports.DTOs;
using EduFocus.Ports.Inbound;
using EduFocus.Ports.Outbound.Services;

namespace EduFocus.WPF.ViewModels;

public enum ReportRangeKind
{
    Day,
    Week,
    ThirtyDays
}

public sealed record ReportRangeOption(ReportRangeKind Kind, string Title);

/// <summary>
/// Day/week/30-day report card for Parent Mode (UC10, issue #36). A report is
/// read on demand, bounded by the use case caps, and never exposes titles,
/// full URLs or process names. Detail retention gaps are surfaced as warnings.
/// </summary>
public sealed partial class ReportViewModel : ObservableObject
{
    public const int MaxSessionRows = 20;
    public const int MaxTimelineRows = 24;
    public const int MaxAttentionEventRows = 10;

    private static readonly string[] WeekdayNames = ["CN", "T2", "T3", "T4", "T5", "T6", "T7"];

    private readonly IReportUseCase _reports;
    private readonly IReportExportUseCase _exports;
    private readonly IAppLogger _logger;
    private readonly TimeProvider _timeProvider;
    private readonly SemaphoreSlim _gate = new(1, 1);

    private StudyReport? _currentReport;
    private bool _loaded;

    [ObservableProperty] private ReportRangeOption? _selectedRange;
    [ObservableProperty] private bool _isLoading = true;
    [ObservableProperty] private bool _isExporting;
    [ObservableProperty] private bool _hasError;
    [ObservableProperty] private string _errorMessage = string.Empty;
    [ObservableProperty] private bool _hasSessions;
    [ObservableProperty] private string _exportMessage = string.Empty;
    [ObservableProperty] private bool _hasExportMessage;
    [ObservableProperty] private bool _exportIsError;
    [ObservableProperty] private string _periodLabel = string.Empty;
    [ObservableProperty] private string _studyTimeText = "0 phút";
    [ObservableProperty] private string _activeTimeText = "0 phút";
    [ObservableProperty] private string _distractionTimeText = "0 phút";
    [ObservableProperty] private string _onlineClassTimeText = "0 phút";
    [ObservableProperty] private string _attentionText = "Chưa có dữ liệu";
    [ObservableProperty] private string _coverageText = "Chưa có dữ liệu";
    [ObservableProperty] private string _confidenceText = "Chưa có dữ liệu";
    [ObservableProperty] private string _monitoredTimeText = "0 phút";
    [ObservableProperty] private string _sessionCountText = "0 phiên";
    [ObservableProperty] private string _blockedAttemptsText = "0";
    [ObservableProperty] private string _warningText = string.Empty;
    [ObservableProperty] private bool _hasWarnings;
    [ObservableProperty] private string _dataNote = string.Empty;
    [ObservableProperty] private string _daysHeader = "Theo ngày";
    [ObservableProperty] private string _sessionsHeader = "Phiên học";
    [ObservableProperty] private string _timelineHeader = "Hoạt động theo giờ";
    [ObservableProperty] private string _attentionHeader = "Sự kiện tập trung";
    [ObservableProperty] private IReadOnlyList<ReportDayRow> _dayRows = [];
    [ObservableProperty] private IReadOnlyList<ReportSessionRow> _sessionRows = [];
    [ObservableProperty] private IReadOnlyList<ReportTimelineRow> _timelineRows = [];
    [ObservableProperty] private IReadOnlyList<ReportTopActivityRow> _topActivities = [];
    [ObservableProperty] private IReadOnlyList<ReportAttentionEventRow> _attentionEventRows = [];
    [ObservableProperty] private string _lastUpdatedText = string.Empty;

    public ReportViewModel(
        IReportUseCase reports,
        IReportExportUseCase exports,
        IAppLogger logger,
        TimeProvider timeProvider)
    {
        _reports = reports ?? throw new ArgumentNullException(nameof(reports));
        _exports = exports ?? throw new ArgumentNullException(nameof(exports));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _timeProvider = timeProvider ?? throw new ArgumentNullException(nameof(timeProvider));

        Ranges =
        [
            new ReportRangeOption(ReportRangeKind.Day, "Hôm nay"),
            new ReportRangeOption(ReportRangeKind.Week, "Tuần này"),
            new ReportRangeOption(ReportRangeKind.ThirtyDays, "30 ngày gần nhất")
        ];
        _selectedRange = Ranges[0];
    }

    public IReadOnlyList<ReportRangeOption> Ranges { get; }

    public bool IsNotLoading => !IsLoading;

    public bool ShowSummary => !IsLoading && !HasError && HasSessions;

    public bool ShowEmptyState => !IsLoading && !HasError && !HasSessions;

    public string ExportButtonText => IsExporting ? "Đang xuất..." : "Xuất CSV";

    private bool CanExport() =>
        !IsLoading && !IsExporting && !HasError && _currentReport is { HasSessions: true };

    public bool HasDayRows => DayRows.Count > 0;

    public bool HasSessionRows => SessionRows.Count > 0;

    public bool HasTimelineRows => TimelineRows.Count > 0;

    public bool HasTopActivities => TopActivities.Count > 0;

    public bool HasAttentionEventRows => AttentionEventRows.Count > 0;

    partial void OnSelectedRangeChanged(ReportRangeOption? value)
    {
        if (value is null)
        {
            // WPF may clear SelectedItem while the ItemsSource binding settles.
            SelectedRange = Ranges[0];
            return;
        }

        if (_loaded)
            _ = RefreshAsync();
    }

    partial void OnIsLoadingChanged(bool value)
    {
        OnPropertyChanged(nameof(IsNotLoading));
        OnPropertyChanged(nameof(ShowSummary));
        OnPropertyChanged(nameof(ShowEmptyState));
        ExportCommand.NotifyCanExecuteChanged();
    }

    partial void OnIsExportingChanged(bool value)
    {
        OnPropertyChanged(nameof(ExportButtonText));
        ExportCommand.NotifyCanExecuteChanged();
    }

    partial void OnHasErrorChanged(bool value)
    {
        OnPropertyChanged(nameof(ShowSummary));
        OnPropertyChanged(nameof(ShowEmptyState));
        ExportCommand.NotifyCanExecuteChanged();
    }

    partial void OnHasSessionsChanged(bool value)
    {
        OnPropertyChanged(nameof(ShowSummary));
        OnPropertyChanged(nameof(ShowEmptyState));
        ExportCommand.NotifyCanExecuteChanged();
    }

    partial void OnDayRowsChanged(IReadOnlyList<ReportDayRow> value) =>
        OnPropertyChanged(nameof(HasDayRows));

    partial void OnSessionRowsChanged(IReadOnlyList<ReportSessionRow> value) =>
        OnPropertyChanged(nameof(HasSessionRows));

    partial void OnTimelineRowsChanged(IReadOnlyList<ReportTimelineRow> value) =>
        OnPropertyChanged(nameof(HasTimelineRows));

    partial void OnTopActivitiesChanged(IReadOnlyList<ReportTopActivityRow> value) =>
        OnPropertyChanged(nameof(HasTopActivities));

    partial void OnAttentionEventRowsChanged(IReadOnlyList<ReportAttentionEventRow> value) =>
        OnPropertyChanged(nameof(HasAttentionEventRows));

    /// <summary>
    /// Reads the selected period. Calls are serialized, so a period change made
    /// while a read is running queues the newest selection instead of dropping it.
    /// </summary>
    public async Task RefreshAsync(CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);

        try
        {
            IsLoading = true;
            HasError = false;
            ErrorMessage = string.Empty;
            HasExportMessage = false;
            ExportMessage = string.Empty;
            _currentReport = null;
            ExportCommand.NotifyCanExecuteChanged();

            var range = SelectedRange ?? Ranges[0];
            var today = DateOnly.FromDateTime(_timeProvider.GetLocalNow().DateTime);
            var report = await _reports
                .GetAsync(BuildPeriod(range.Kind, today), cancellationToken)
                .ConfigureAwait(false);

            Apply(report, range);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
        catch (Exception ex)
        {
            _logger.Error(LogCategories.UI, "Could not load the study report.", ex);
            HasError = true;
            ErrorMessage = "Không thể đọc báo cáo học tập. Hãy thử lại.";
        }
        finally
        {
            _loaded = true;
            IsLoading = false;
            _gate.Release();
        }
    }

    [RelayCommand]
    private Task ReloadAsync() => RefreshAsync();

    [RelayCommand(CanExecute = nameof(CanExport))]
    private async Task ExportAsync()
    {
        if (_currentReport is not { } report)
            return;

        IsExporting = true;
        HasExportMessage = false;
        ExportMessage = string.Empty;

        try
        {
            var result = await _exports.ExportCsvAsync(report).ConfigureAwait(false);
            ExportIsError = false;
            ExportMessage = BuildExportMessage(result);
        }
        catch (Exception ex)
        {
            _logger.Error(LogCategories.UI, "Could not export the study report.", ex);
            ExportIsError = true;
            ExportMessage = "Không thể xuất CSV. Hãy kiểm tra quyền ghi và dung lượng ổ đĩa rồi thử lại.";
        }
        finally
        {
            IsExporting = false;
            HasExportMessage = true;
        }
    }

    private void Apply(StudyReport report, ReportRangeOption range)
    {
        _currentReport = report;
        ExportCommand.NotifyCanExecuteChanged();

        HasError = false;
        ErrorMessage = string.Empty;
        HasSessions = report.HasSessions;
        PeriodLabel = BuildPeriodLabel(range.Kind, report.Period);
        StudyTimeText = FormatDuration(report.StudySeconds);
        ActiveTimeText = FormatDuration(report.ActiveSeconds);
        DistractionTimeText = FormatDuration(report.DistractionSeconds);
        OnlineClassTimeText = FormatDuration(report.OnlineClassSeconds);
        AttentionText = FormatPercent(report.Attention.AttentionPercent);
        CoverageText = FormatPercent(report.Attention.CoveragePercent);
        ConfidenceText = FormatPercent(report.ConfidenceAvg is { } confidence ? confidence * 100 : null);
        MonitoredTimeText = FormatDuration(report.Attention.MonitoredSeconds);
        SessionCountText = FormatSessionCount(report.SessionCount);
        BlockedAttemptsText = report.BlockedAttemptCount.ToString(CultureInfo.InvariantCulture);

        var warnings = new List<string>();
        if (report.Attention.IsCoverageLow && report.Attention.Warning is { Length: > 0 } warning)
            warnings.Add(warning);
        if (report.WarningText.Length > 0)
            warnings.Add(report.WarningText);
        WarningText = string.Join(" ", warnings);
        HasWarnings = WarningText.Length > 0;

        DataNote = BuildDataNote(report);

        var daysWithSessions = report.Days.Where(day => day.HasSessions).ToArray();
        DayRows = daysWithSessions.Select(BuildDayRow).ToArray();
        DaysHeader = daysWithSessions.Length < report.Days.Count
            ? $"Theo ngày · {daysWithSessions.Length}/{report.Days.Count} ngày có phiên học"
            : "Theo ngày";

        SessionRows = report.Sessions.Take(MaxSessionRows).Select(BuildSessionRow).ToArray();
        SessionsHeader = report.SessionCount > SessionRows.Count
            ? $"Phiên học · {SessionRows.Count}/{report.SessionCount} phiên gần nhất"
            : "Phiên học";

        var buckets = report.Timeline;
        TimelineRows = buckets
            .Skip(Math.Max(0, buckets.Count - MaxTimelineRows))
            .Select(BuildTimelineRow)
            .ToArray();
        TimelineHeader = report.TimelineTruncated
            ? "Hoạt động theo giờ · dữ liệu mẫu đã được giới hạn"
            : buckets.Count > TimelineRows.Count
                ? $"Hoạt động theo giờ · {TimelineRows.Count}/{buckets.Count} khung giờ gần nhất"
                : "Hoạt động theo giờ";

        TopActivities = report.TopActivities.Select(BuildTopActivityRow).ToArray();

        AttentionEventRows = report.AttentionEvents
            .Take(MaxAttentionEventRows)
            .Select(BuildAttentionEventRow)
            .ToArray();
        AttentionHeader = report.AttentionEventsTruncated
            ? $"Sự kiện tập trung · {AttentionEventRows.Count} sự kiện mới nhất (dữ liệu đã được giới hạn)"
            : report.AttentionEvents.Count > AttentionEventRows.Count
                ? $"Sự kiện tập trung · {AttentionEventRows.Count}/{report.AttentionEvents.Count} sự kiện mới nhất"
                : "Sự kiện tập trung";

        LastUpdatedText = $"Cập nhật lúc {_timeProvider.GetLocalNow():HH:mm:ss}";
    }

    private ReportDayRow BuildDayRow(DailyStudySummary day)
    {
        var attention = day.Attention;
        var title = $"{WeekdayNames[(int)day.LocalDate.DayOfWeek]}, {day.LocalDate:dd/MM} · {FormatSessionCount(day.SessionCount)}";
        var attentionText = attention.AttentionPercent is { } percent
            ? $"Tập trung {percent:0}% · Độ phủ {FormatPercent(attention.CoveragePercent)}"
            : "Chưa có dữ liệu tập trung";

        return new ReportDayRow(title, $"Học {FormatDuration(day.StudySeconds)}", attentionText);
    }

    private ReportSessionRow BuildSessionRow(StudySession session)
    {
        var start = ToLocal(session.StartedAtUtc);
        var time = session.IsActive
            ? $"Từ {start:HH:mm}"
            : $"{start:HH:mm} – {ToLocal(session.EndedAtUtc ?? session.LastHeartbeatAtUtc):HH:mm}";

        var activity = new List<string>();
        if (session.StudySeconds > 0)
            activity.Add($"Học {FormatDuration(session.StudySeconds)}");
        if (session.DistractionSeconds > 0)
            activity.Add($"Mất tập trung {FormatDuration(session.DistractionSeconds)}");
        if (session.NeutralSeconds > 0)
            activity.Add($"Trung tính {FormatDuration(session.NeutralSeconds)}");
        if (session.OnlineClassSeconds > 0)
            activity.Add($"Lớp trực tuyến {FormatDuration(session.OnlineClassSeconds)}");

        var attention = session.AttentionPercent is { } percent
            ? $"Tập trung {percent:0}%"
            : "Chưa có dữ liệu tập trung";
        if (session.CoveragePercent is { } coverage)
            attention += $" · Độ phủ {coverage:0}%";
        if (session.ConfidenceAvg is { } confidence)
            attention += $" · Tin cậy {confidence * 100:0}%";

        return new ReportSessionRow(
            time,
            $"{StatusTitle(session.Status)} · {TriggerTitle(session.Trigger)}",
            activity.Count > 0 ? string.Join(" · ", activity) : "Chưa có dữ liệu hoạt động.",
            attention);
    }

    private static ReportTimelineRow BuildTimelineRow(HourlyActivityBucket bucket)
    {
        var activity = new List<string>();
        if (bucket.StudySeconds > 0)
            activity.Add($"Học {FormatDuration(bucket.StudySeconds)}");
        if (bucket.DistractionSeconds > 0)
            activity.Add($"Mất tập trung {FormatDuration(bucket.DistractionSeconds)}");
        if (bucket.NeutralSeconds > 0)
            activity.Add($"Trung tính {FormatDuration(bucket.NeutralSeconds)}");

        var time = $"{WeekdayNames[(int)bucket.LocalDate.DayOfWeek]}, {bucket.LocalDate:dd/MM} · {bucket.LocalHour:00}:00";
        return new ReportTimelineRow(time, string.Join(" · ", activity), FormatDuration(bucket.TotalSeconds));
    }

    private static ReportTopActivityRow BuildTopActivityRow(TopActivityItem item) =>
        new(item.Name, $"{KindTitle(item.Kind)} · {FormatDuration(item.Seconds)}");

    private ReportAttentionEventRow BuildAttentionEventRow(AttentionEvent attentionEvent)
    {
        var local = ToLocal(attentionEvent.StartedAtUtc);
        var details = new List<string>
        {
            attentionEvent.IsOpen ? "Đang diễn ra" : FormatDuration(attentionEvent.DurationSeconds)
        };

        if (attentionEvent.Confidence is { } confidence)
            details.Add($"Tin cậy {confidence * 100:0}%");
        if (attentionEvent.Domain is { } domain)
            details.Add(domain.Value);
        else if (!string.IsNullOrWhiteSpace(attentionEvent.AppName))
            details.Add(attentionEvent.AppName);

        return new ReportAttentionEventRow(
            $"{WeekdayNames[(int)local.DayOfWeek]}, {local:dd/MM} · {local:HH:mm}",
            AttentionStateTitle(attentionEvent.State),
            string.Join(" · ", details));
    }

    private static string BuildDataNote(StudyReport report)
    {
        var notes = new List<string>();

        if (report.TimelineTruncated)
            notes.Add("Dữ liệu hoạt động vượt giới hạn đọc; timeline chỉ dùng các mẫu gần nhất.");

        notes.Add(StudyReport.BlockedAttemptsNote);
        notes.Add("Báo cáo chỉ hiển thị tên ứng dụng và tên miền đã làm sạch; không có tiêu đề cửa sổ, URL đầy đủ hay nội dung trang.");
        return string.Join(" ", notes);
    }

    private static string BuildExportMessage(ReportExportResult result)
    {
        var message =
            $"Đã lưu CSV tại: {result.FilePath}. " +
            "File chứa dữ liệu học tập nhạy cảm; hãy bảo vệ file và chỉ chia sẻ với người bạn tin tưởng.";

        if (result.Warnings.Count > 0)
            message += " " + string.Join(" ", result.Warnings);

        return message;
    }

    private static ReportPeriod BuildPeriod(ReportRangeKind kind, DateOnly today) =>
        kind switch
        {
            ReportRangeKind.Week => ReportPeriod.Week(today),
            ReportRangeKind.ThirtyDays => ReportPeriod.Range(today.AddDays(-29), today),
            _ => ReportPeriod.Day(today)
        };

    private static string BuildPeriodLabel(ReportRangeKind kind, ReportPeriod period) =>
        kind switch
        {
            ReportRangeKind.Day => $"Hôm nay, {period.Start:dd/MM/yyyy}",
            ReportRangeKind.Week => $"Tuần {period.Start:dd/MM/yyyy} – {period.End:dd/MM/yyyy}",
            _ => $"30 ngày {period.Start:dd/MM/yyyy} – {period.End:dd/MM/yyyy}"
        };

    private static string FormatPercent(double? percent) =>
        percent is { } value ? $"{value:0}%" : "Chưa có dữ liệu";

    private static string FormatSessionCount(int count) =>
        count == 1 ? "1 phiên" : $"{count} phiên";

    private static string StatusTitle(StudySessionStatus status) =>
        status switch
        {
            StudySessionStatus.Active => "Đang diễn ra",
            StudySessionStatus.Completed => "Hoàn tất",
            StudySessionStatus.Aborted => "Đã hủy",
            _ => "Bị gián đoạn"
        };

    private static string TriggerTitle(SessionTrigger trigger) =>
        trigger switch
        {
            SessionTrigger.Schedule => "Theo lịch",
            SessionTrigger.Recovery => "Khôi phục",
            _ => "Thủ công"
        };

    private static string AttentionStateTitle(AttentionState state) =>
        state switch
        {
            AttentionState.Focused => "Tập trung",
            AttentionState.Distracted => "Mất tập trung",
            AttentionState.Absent => "Vắng mặt",
            AttentionState.Drowsy => "Buồn ngủ",
            _ => "Camera không khả dụng"
        };

    private static string KindTitle(TopActivityKind kind) =>
        kind switch
        {
            TopActivityKind.Domain => "Tên miền",
            TopActivityKind.App => "Ứng dụng",
            _ => "Không xác định"
        };

    private DateTime ToLocal(DateTime utc) =>
        TimeZoneInfo.ConvertTimeFromUtc(AsUtc(utc), _timeProvider.LocalTimeZone);

    private static DateTime AsUtc(DateTime value) =>
        value.Kind == DateTimeKind.Utc ? value : DateTime.SpecifyKind(value, DateTimeKind.Utc);

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
}

public sealed record ReportDayRow(string TitleText, string StudyText, string AttentionText);

public sealed record ReportSessionRow(
    string TimeText,
    string StatusText,
    string ActivityText,
    string AttentionText);

public sealed record ReportTimelineRow(string TimeText, string ActivityText, string TotalText);

public sealed record ReportTopActivityRow(string Name, string DetailText);

public sealed record ReportAttentionEventRow(string TimeText, string StateText, string DetailText);
