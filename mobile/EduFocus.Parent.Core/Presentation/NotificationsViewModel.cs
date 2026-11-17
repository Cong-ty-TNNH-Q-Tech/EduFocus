using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using EduFocus.Cloud.Contracts.Notifications;
using EduFocus.Cloud.Contracts.Preferences;

namespace EduFocus.Parent.Core.Presentation;

public sealed partial class NotificationsViewModel(
    IParentDataService data,
    ParentDataCache cache,
    AppState appState,
    SessionExpiryService session) : ObservableObject
{
    public const int PageSize = 50;

    [ObservableProperty] private bool _isLoading;
    [ObservableProperty] private bool _isRefreshing;
    [ObservableProperty] private string _errorMessage = string.Empty;
    [ObservableProperty] private bool _isShowingCachedData;
    [ObservableProperty] private string _cachedBannerText = string.Empty;
    [ObservableProperty] private IReadOnlyList<NotificationItem> _items = [];
    [ObservableProperty] private bool _hasItems;
    [ObservableProperty] private bool _showEmptyState;
    [ObservableProperty] private bool _canLoadMore;
    [ObservableProperty] private bool _isLoadingMore;
    [ObservableProperty] private bool _canEditPreferences;
    [ObservableProperty] private bool _attentionAlert;
    [ObservableProperty] private bool _dailySummary;
    [ObservableProperty] private bool _protectionOff;
    [ObservableProperty] private bool _securityAlert;
    [ObservableProperty] private TimeSpan _quietStart = TimeSpan.FromHours(22);
    [ObservableProperty] private TimeSpan _quietEnd = TimeSpan.FromHours(6);
    [ObservableProperty] private TimeSpan _dailySummaryTime = new(20, 30, 0);
    [ObservableProperty] private bool _isSavingPreferences;
    [ObservableProperty] private string _preferencesStatus = string.Empty;
    [ObservableProperty] private bool _preferencesStatusIsError;

    public bool HasError => ErrorMessage.Length > 0;

    public bool HasPreferencesStatus => PreferencesStatus.Length > 0;

    private string? _nextBefore;
    private string _timeZoneId = TimeZoneInfo.Local.Id;
    private List<NotificationItem> _loaded = [];

    partial void OnErrorMessageChanged(string value) => OnPropertyChanged(nameof(HasError));

    partial void OnPreferencesStatusChanged(string value) =>
        OnPropertyChanged(nameof(HasPreferencesStatus));

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

    [RelayCommand]
    private async Task LoadMoreAsync()
    {
        if (IsLoadingMore || _nextBefore is null)
            return;

        IsLoadingMore = true;
        ErrorMessage = string.Empty;
        try
        {
            var page = await data.GetNotificationsAsync(PageSize, _nextBefore);
            _nextBefore = page.NextBefore;
            ApplyPage(page.Items, replace: false);
        }
        catch (Exception exception)
        {
            if (CloudErrors.IsUnauthorized(exception))
                await session.HandleUnauthorizedAsync();

            ErrorMessage = CloudErrors.Describe(exception);
        }
        finally
        {
            IsLoadingMore = false;
            CanLoadMore = _nextBefore is not null;
        }
    }

    [RelayCommand]
    private async Task SavePreferencesAsync()
    {
        if (IsSavingPreferences || !CanEditPreferences)
            return;

        IsSavingPreferences = true;
        PreferencesStatus = string.Empty;
        PreferencesStatusIsError = false;
        try
        {
            var updated = await data.UpdatePreferencesAsync(new UpdateNotificationPreferencesRequest(
                AttentionAlert,
                DailySummary,
                ProtectionOff,
                SecurityAlert,
                ToMinutes(QuietStart),
                ToMinutes(QuietEnd),
                ToMinutes(DailySummaryTime),
                _timeZoneId));

            ApplyPreferences(updated);
            PreferencesStatus = "Đã lưu tùy chọn thông báo.";
        }
        catch (Exception exception)
        {
            if (CloudErrors.IsUnauthorized(exception))
                await session.HandleUnauthorizedAsync();

            PreferencesStatus = CloudErrors.IsUnauthorized(exception)
                ? CloudErrors.SessionExpired
                : "Không lưu được tùy chọn. Kiểm tra kết nối và thử lại.";
            PreferencesStatusIsError = true;
        }
        finally
        {
            IsSavingPreferences = false;
        }
    }

    private async Task LoadCoreAsync(bool refresh)
    {
        if (refresh)
            IsRefreshing = true;
        else
            IsLoading = true;

        var zone = FamilyCalendar.Resolve(appState.TimeZone);
        ErrorMessage = string.Empty;

        try
        {
            if (!appState.IsLinked)
            {
                _nextBefore = null;
                ApplyPage([], replace: true);
                return;
            }

            var page = await data.GetNotificationsAsync(PageSize, before: null);
            cache.SaveNotifications(page);
            _nextBefore = page.NextBefore;
            IsShowingCachedData = false;
            CachedBannerText = string.Empty;
            ApplyPage(page.Items, replace: true);
        }
        catch (Exception exception)
        {
            if (CloudErrors.IsUnauthorized(exception))
                await session.HandleUnauthorizedAsync();

            ErrorMessage = CloudErrors.Describe(exception);
            if (cache.TryGetNotifications(out var cachedPage, out var savedAt))
            {
                IsShowingCachedData = true;
                CachedBannerText =
                    $"Đang xem dữ liệu lưu lúc {VietnameseFormat.DateTimeLocal(savedAt, zone)}.";
                _nextBefore = cachedPage.NextBefore;
                ApplyPage(cachedPage.Items, replace: true);
            }
            else
            {
                IsShowingCachedData = false;
                CachedBannerText = string.Empty;
                _nextBefore = null;
                ApplyPage([], replace: true);
            }
        }
        finally
        {
            IsLoading = false;
            IsRefreshing = false;
        }

        if (appState.IsLinked)
            await LoadPreferencesAsync();
    }

    private async Task LoadPreferencesAsync()
    {
        try
        {
            var preferences = await data.GetPreferencesAsync();
            ApplyPreferences(preferences);
            CanEditPreferences = true;
            PreferencesStatus = string.Empty;
            PreferencesStatusIsError = false;
        }
        catch (Exception exception)
        {
            if (CloudErrors.IsUnauthorized(exception))
                await session.HandleUnauthorizedAsync();

            CanEditPreferences = false;
            PreferencesStatus = CloudErrors.IsUnauthorized(exception)
                ? CloudErrors.SessionExpired
                : "Không tải được tùy chọn thông báo. Kéo xuống để thử lại.";
            PreferencesStatusIsError = true;
        }
    }

    private void ApplyPreferences(NotificationPreferencesDto preferences)
    {
        _timeZoneId = string.IsNullOrWhiteSpace(preferences.TimeZoneId)
            ? appState.TimeZone ?? TimeZoneInfo.Local.Id
            : preferences.TimeZoneId;

        AttentionAlert = preferences.AttentionAlert;
        DailySummary = preferences.DailySummary;
        ProtectionOff = preferences.ProtectionOff;
        SecurityAlert = preferences.SecurityAlert;
        QuietStart = TimeOf(preferences.QuietStartMinutes);
        QuietEnd = TimeOf(preferences.QuietEndMinutes);
        DailySummaryTime = TimeOf(preferences.DailySummaryMinutes);
    }

    private void ApplyPage(IReadOnlyList<NotificationDto> notifications, bool replace)
    {
        var zone = FamilyCalendar.Resolve(appState.TimeZone);
        var mapped = notifications.Select(n => new NotificationItem(n, zone)).ToList();
        _loaded = replace ? mapped : [.. _loaded, .. mapped];
        Items = _loaded;
        HasItems = _loaded.Count > 0;
        ShowEmptyState = !HasItems;
        CanLoadMore = _nextBefore is not null;
    }

    private static TimeSpan TimeOf(int minutes) =>
        TimeSpan.FromMinutes(Math.Clamp(minutes, 0, 1439));

    private static int ToMinutes(TimeSpan time) =>
        Math.Clamp((int)time.TotalMinutes, 0, 1439);
}
