using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using EduFocus.Cloud.Contracts.Reports;

namespace EduFocus.Parent.Core.Presentation;

public sealed partial class ReportsViewModel(
    IParentDataService data,
    ParentDataCache cache,
    AppState appState,
    SessionExpiryService session,
    Func<DateTimeOffset>? clock = null) : ObservableObject
{
    public const int RangeDays = 7;

    [ObservableProperty] private bool _isLoading;
    [ObservableProperty] private bool _isRefreshing;
    [ObservableProperty] private string _errorMessage = string.Empty;
    [ObservableProperty] private bool _isShowingCachedData;
    [ObservableProperty] private string _cachedBannerText = string.Empty;
    [ObservableProperty] private IReadOnlyList<ReportDayItem> _days = [];
    [ObservableProperty] private IReadOnlyList<ChartBarItem> _chartBars = [];
    [ObservableProperty] private bool _hasDays;
    [ObservableProperty] private bool _showEmptyState;
    [ObservableProperty] private string _rangeText = string.Empty;
    [ObservableProperty] private string _totalStudyText = "—";
    [ObservableProperty] private string _averageStudyText = "—";
    [ObservableProperty] private bool _hasSelection;
    [ObservableProperty] private string _selectedDateText = string.Empty;
    [ObservableProperty] private string _selectedStudyText = "—";
    [ObservableProperty] private string _selectedAttentionText = "—";
    [ObservableProperty] private string _selectedCoverageText = "—";
    [ObservableProperty] private string _selectedBlockedText = "—";
    [ObservableProperty] private double _selectedAttentionPercentValue;
    [ObservableProperty] private double _selectedCoveragePercentValue;
    [ObservableProperty] private bool _selectedShowCoverageWarning;

    public bool HasError => ErrorMessage.Length > 0;

    private DateOnly? _selectedDate;

    partial void OnErrorMessageChanged(string value) => OnPropertyChanged(nameof(HasError));

    public async Task LoadAsync(DateOnly? focusDate = null)
    {
        if (IsLoading || IsRefreshing)
            return;

        await LoadCoreAsync(refresh: false, focusDate);
    }

    [RelayCommand]
    private async Task RefreshAsync()
    {
        if (IsLoading)
        {
            IsRefreshing = false;
            return;
        }

        if (IsRefreshing)
            return;

        await LoadCoreAsync(refresh: true, _selectedDate);
    }

    [RelayCommand]
    private void Select(ReportDayItem? item)
    {
        if (item is null)
            return;

        _selectedDate = item.Date;
        SelectedDateText = item.DateText;
        SelectedStudyText = item.StudyText;
        SelectedAttentionText = item.AttentionText;
        SelectedCoverageText = item.CoverageText;
        SelectedBlockedText = item.BlockedText;
        SelectedAttentionPercentValue = Fraction(item.Summary.AttentionPercent);
        SelectedCoveragePercentValue = Fraction(item.Summary.CoveragePercent);
        SelectedShowCoverageWarning = item.ShowCoverageWarning;
        HasSelection = true;
    }

    private async Task LoadCoreAsync(bool refresh, DateOnly? focusDate)
    {
        if (refresh)
            IsRefreshing = true;
        else
            IsLoading = true;

        var zone = FamilyCalendar.Resolve(appState.TimeZone);
        var today = FamilyCalendar.Today(clock?.Invoke() ?? DateTimeOffset.UtcNow, appState.TimeZone);
        var from = today.AddDays(-(RangeDays - 1));
        RangeText = $"Từ {VietnameseFormat.DateShort(from)} đến {VietnameseFormat.DateShort(today)}";
        ErrorMessage = string.Empty;

        try
        {
            if (!appState.IsLinked)
            {
                ApplyDays([], today, focusDate);
                return;
            }

            var days = await data.GetDailyAsync(from, today);
            cache.SaveDaily(days);
            IsShowingCachedData = false;
            CachedBannerText = string.Empty;
            ApplyDays(days, today, focusDate);
        }
        catch (Exception exception)
        {
            if (CloudErrors.IsUnauthorized(exception))
                await session.HandleUnauthorizedAsync();

            ErrorMessage = CloudErrors.Describe(exception);
            if (cache.TryGetDaily(out var cached, out var savedAt))
            {
                IsShowingCachedData = true;
                CachedBannerText =
                    $"Đang xem dữ liệu lưu lúc {VietnameseFormat.DateTimeLocal(savedAt, zone)}.";
                ApplyDays(cached, today, focusDate);
            }
            else
            {
                IsShowingCachedData = false;
                CachedBannerText = string.Empty;
                ApplyDays([], today, focusDate);
            }
        }
        finally
        {
            IsLoading = false;
            IsRefreshing = false;
        }
    }

    private void ApplyDays(
        IReadOnlyList<DailySummaryDto> summaries,
        DateOnly today,
        DateOnly? focusDate)
    {
        var ordered = summaries
            .GroupBy(s => s.LocalDate)
            .Select(group => group.OrderByDescending(s => s.GeneratedAtUtc).First())
            .OrderByDescending(s => s.LocalDate)
            .ToList();

        Days = ordered.Select(s => new ReportDayItem(s, today)).ToList();
        ChartBars = ordered
            .OrderBy(s => s.LocalDate)
            .Select(s => new ChartBarItem(VietnameseFormat.DateShort(s.LocalDate), s.StudySeconds, s.LocalDate == today))
            .ToList();
        HasDays = Days.Count > 0;
        ShowEmptyState = !HasDays;
        TotalStudyText = ordered.Count == 0
            ? "—"
            : VietnameseFormat.Duration(ordered.Sum(s => s.StudySeconds));
        AverageStudyText = ordered.Count == 0
            ? "—"
            : VietnameseFormat.Duration((long)Math.Round(ordered.Average(s => (double)s.StudySeconds)));

        var selection =
            (focusDate is { } focus ? Days.FirstOrDefault(d => d.Date == focus) : null)
            ?? Days.FirstOrDefault(d => d.IsToday)
            ?? Days.FirstOrDefault();

        if (selection is null)
        {
            HasSelection = false;
            _selectedDate = null;
            SelectedDateText = string.Empty;
            SelectedStudyText = "—";
            SelectedAttentionText = "—";
            SelectedCoverageText = "—";
            SelectedBlockedText = "—";
            SelectedAttentionPercentValue = 0;
            SelectedCoveragePercentValue = 0;
            SelectedShowCoverageWarning = false;
            return;
        }

        Select(selection);
    }

    private static double Fraction(double? percent) =>
        percent is { } value ? Math.Clamp(value / 100d, 0d, 1d) : 0d;
}
