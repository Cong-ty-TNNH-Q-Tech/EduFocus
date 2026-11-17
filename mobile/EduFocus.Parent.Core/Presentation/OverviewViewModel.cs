using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using EduFocus.Cloud.Contracts.Reports;

namespace EduFocus.Parent.Core.Presentation;

public sealed partial class OverviewViewModel(
    IParentDataService data,
    ParentDataCache cache,
    AppState appState,
    SessionExpiryService session,
    Func<DateTimeOffset>? clock = null) : ObservableObject
{
    [ObservableProperty] private bool _isLoading;
    [ObservableProperty] private bool _isRefreshing;
    [ObservableProperty] private string _errorMessage = string.Empty;
    [ObservableProperty] private bool _isShowingCachedData;
    [ObservableProperty] private string _cachedBannerText = string.Empty;
    [ObservableProperty] private bool _hasSummary;
    [ObservableProperty] private bool _showEmptyState;
    [ObservableProperty] private string _todayText = string.Empty;
    [ObservableProperty] private string _studyTimeText = "—";
    [ObservableProperty] private string _activeTimeText = "—";
    [ObservableProperty] private string _onlineClassTimeText = "—";
    [ObservableProperty] private string _sessionsText = "—";
    [ObservableProperty] private string _blockedText = "—";
    [ObservableProperty] private string _attentionText = "—";
    [ObservableProperty] private string _coverageText = "—";
    [ObservableProperty] private bool _showCoverageWarning;
    [ObservableProperty] private string _lastUpdatedText = string.Empty;

    public bool HasError => ErrorMessage.Length > 0;

    partial void OnErrorMessageChanged(string value) => OnPropertyChanged(nameof(HasError));

    public async Task LoadAsync()
    {
        if (IsLoading || IsRefreshing)
            return;

        await LoadCoreAsync(refresh: false);
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

        await LoadCoreAsync(refresh: true);
    }

    private async Task LoadCoreAsync(bool refresh)
    {
        if (refresh)
            IsRefreshing = true;
        else
            IsLoading = true;

        var zone = FamilyCalendar.Resolve(appState.TimeZone);
        var today = FamilyCalendar.Today(clock?.Invoke() ?? DateTimeOffset.UtcNow, appState.TimeZone);
        TodayText = $"Hôm nay, {VietnameseFormat.DateLong(today)}";
        ErrorMessage = string.Empty;

        try
        {
            if (!appState.IsLinked)
            {
                Apply(null, zone);
                return;
            }

            var days = await data.GetDailyAsync(today, today);
            cache.SaveDaily(days);
            IsShowingCachedData = false;
            CachedBannerText = string.Empty;
            Apply(days.FirstOrDefault(d => d.LocalDate == today), zone);
        }
        catch (Exception exception)
        {
            if (CloudErrors.IsUnauthorized(exception))
                await session.HandleUnauthorizedAsync();

            ErrorMessage = CloudErrors.Describe(exception);
            if (cache.TryGetDaily(out var cached, out var savedAt)
                && cached.FirstOrDefault(d => d.LocalDate == today) is { } cachedToday)
            {
                IsShowingCachedData = true;
                CachedBannerText =
                    $"Đang xem dữ liệu lưu lúc {VietnameseFormat.DateTimeLocal(savedAt, zone)}.";
                Apply(cachedToday, zone);
            }
            else
            {
                IsShowingCachedData = false;
                CachedBannerText = string.Empty;
                Apply(null, zone);
            }
        }
        finally
        {
            IsLoading = false;
            IsRefreshing = false;
        }
    }

    private void Apply(DailySummaryDto? summary, TimeZoneInfo zone)
    {
        HasSummary = summary is not null;
        ShowEmptyState = summary is null;

        if (summary is null)
        {
            StudyTimeText = "—";
            ActiveTimeText = "—";
            OnlineClassTimeText = "—";
            SessionsText = "—";
            BlockedText = "—";
            AttentionText = "—";
            CoverageText = "—";
            ShowCoverageWarning = false;
            LastUpdatedText = string.Empty;
            return;
        }

        StudyTimeText = VietnameseFormat.Duration(summary.StudySeconds);
        ActiveTimeText = VietnameseFormat.Duration(summary.ActiveSeconds);
        OnlineClassTimeText = VietnameseFormat.Duration(summary.OnlineClassSeconds);
        SessionsText = summary.SessionsCount.ToString(CultureInfo.InvariantCulture);
        BlockedText = summary.BlockedAttempts.ToString(CultureInfo.InvariantCulture);
        AttentionText = VietnameseFormat.Percent(summary.AttentionPercent);
        CoverageText = VietnameseFormat.Percent(summary.CoveragePercent);
        ShowCoverageWarning = summary.CoveragePercent is { } coverage && coverage < 50;
        LastUpdatedText =
            $"Cập nhật lúc {VietnameseFormat.DateTimeLocal(summary.GeneratedAtUtc, zone)}";
    }
}
