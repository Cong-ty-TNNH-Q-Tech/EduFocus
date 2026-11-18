using EduFocus.Cloud.Contracts.Notifications;
using EduFocus.Parent.Core.Services;
using EduFocus.Parent.Tests.TestSupport;

namespace EduFocus.Parent.Tests.Presentation;

public sealed class NotificationsViewModelTests
{
    [Fact]
    public async Task Loads_first_page_and_preferences()
    {
        var harness = new ScreenHarness();
        harness.Api.NotificationPages.Add(new NotificationListResponse(
            [TestNotifications.Notification()],
            "cursor-1"));
        harness.Api.PreferencesResult = new(
            true, false, true, true, 21 * 60, 6 * 60, 20 * 60, "Asia/Ho_Chi_Minh");

        var viewModel = harness.Notifications();
        await viewModel.LoadAsync();

        var request = Assert.Single(harness.Api.NotificationRequests);
        Assert.Equal(50, request.Limit);
        Assert.Null(request.Before);

        var item = Assert.Single(viewModel.Items);
        Assert.Equal("Tổng kết ngày", item.CategoryText);
        Assert.Equal("17:30 17/11/2026", item.TimeText);
        Assert.Equal("Đã gửi", item.StatusText);
        Assert.True(viewModel.HasItems);
        Assert.False(viewModel.ShowEmptyState);
        Assert.True(viewModel.CanLoadMore);

        Assert.True(viewModel.CanEditPreferences);
        Assert.True(viewModel.AttentionAlert);
        Assert.False(viewModel.DailySummary);
        Assert.Equal(TimeSpan.FromHours(21), viewModel.QuietStart);
        Assert.Equal(TimeSpan.FromMinutes(20 * 60), viewModel.DailySummaryTime);
    }

    [Fact]
    public async Task Load_more_appends_pages_with_cursor()
    {
        var harness = new ScreenHarness();
        harness.Api.NotificationPages.Add(new NotificationListResponse(
            [TestNotifications.Notification()],
            "cursor-1"));
        harness.Api.NotificationPages.Add(new NotificationListResponse(
            [TestNotifications.Notification(NotificationKind.SecurityAlert)],
            null));

        var viewModel = harness.Notifications();
        await viewModel.LoadAsync();
        await viewModel.LoadMoreCommand.ExecuteAsync(null);

        Assert.Equal(2, harness.Api.NotificationRequests.Count);
        Assert.Equal("cursor-1", harness.Api.NotificationRequests[1].Before);
        Assert.Equal(2, viewModel.Items.Count);
        Assert.False(viewModel.CanLoadMore);
        Assert.False(viewModel.IsLoadingMore);
    }

    [Fact]
    public async Task Save_preferences_puts_current_values()
    {
        var harness = new ScreenHarness();
        var viewModel = harness.Notifications();
        await viewModel.LoadAsync();

        viewModel.DailySummary = false;
        viewModel.QuietStart = new TimeSpan(21, 15, 0);
        viewModel.DailySummaryTime = new TimeSpan(19, 45, 0);
        await viewModel.SavePreferencesCommand.ExecuteAsync(null);

        var request = Assert.Single(harness.Api.PreferenceUpdates);
        Assert.False(request.DailySummary);
        Assert.True(request.AttentionAlert);
        Assert.Equal(21 * 60 + 15, request.QuietStartMinutes);
        Assert.Equal(6 * 60, request.QuietEndMinutes);
        Assert.Equal(19 * 60 + 45, request.DailySummaryMinutes);
        Assert.Equal("Asia/Ho_Chi_Minh", request.TimeZoneId);
        Assert.Equal("Đã lưu tùy chọn thông báo.", viewModel.PreferencesStatus);
        Assert.False(viewModel.PreferencesStatusIsError);
        Assert.False(viewModel.IsSavingPreferences);
    }

    [Fact]
    public async Task Save_preferences_preserves_security_alert_bypass_quiet_hours()
    {
        var harness = new ScreenHarness();
        harness.Api.PreferencesResult = new(
            true,
            true,
            true,
            true,
            22 * 60,
            6 * 60,
            20 * 60 + 30,
            "Asia/Ho_Chi_Minh",
            SecurityAlertBypassQuietHours: false);

        var viewModel = harness.Notifications();
        await viewModel.LoadAsync();

        viewModel.DailySummary = false;
        await viewModel.SavePreferencesCommand.ExecuteAsync(null);

        var request = Assert.Single(harness.Api.PreferenceUpdates);
        Assert.False(request.DailySummary);
        Assert.False(request.SecurityAlertBypassQuietHours);
    }

    [Fact]
    public async Task Save_failure_shows_friendly_error()
    {
        var harness = new ScreenHarness();
        var viewModel = harness.Notifications();
        await viewModel.LoadAsync();
        harness.Api.UpdatePreferencesError = new HttpRequestException("offline");

        await viewModel.SavePreferencesCommand.ExecuteAsync(null);

        Assert.True(viewModel.PreferencesStatusIsError);
        Assert.Contains("Không lưu được tùy chọn", viewModel.PreferencesStatus);
    }

    [Fact]
    public async Task Error_falls_back_to_cached_notifications()
    {
        var harness = new ScreenHarness();
        harness.Cache.SaveNotifications(new NotificationListResponse(
            [TestNotifications.Notification(NotificationKind.AttentionAlert)],
            null));
        harness.Api.NotificationsError = new HttpRequestException("offline");

        var viewModel = harness.Notifications();
        await viewModel.LoadAsync();

        Assert.True(viewModel.HasError);
        Assert.True(viewModel.IsShowingCachedData);
        Assert.True(viewModel.HasItems);
        Assert.Equal("Cảnh báo tập trung", Assert.Single(viewModel.Items).CategoryText);
        Assert.NotEmpty(viewModel.CachedBannerText);
    }

    [Fact]
    public async Task Empty_history_shows_empty_state()
    {
        var harness = new ScreenHarness();

        var viewModel = harness.Notifications();
        await viewModel.LoadAsync();

        Assert.False(viewModel.HasItems);
        Assert.True(viewModel.ShowEmptyState);
        Assert.False(viewModel.CanLoadMore);
        Assert.True(viewModel.CanEditPreferences);
    }

    [Fact]
    public async Task Unauthorized_signals_session_expiry()
    {
        var harness = new ScreenHarness();
        harness.Api.NotificationsError = new CloudApiException(401, "invalid_token", "Hết phiên.");
        var raised = 0;
        harness.Session.Expired += () => raised++;

        var viewModel = harness.Notifications();
        await viewModel.LoadAsync();

        Assert.Equal(1, raised);
        Assert.False(harness.State.IsLinked);
        Assert.Equal(Core.Presentation.CloudErrors.SessionExpired, viewModel.ErrorMessage);
        Assert.False(viewModel.CanEditPreferences);
    }

    [Fact]
    public async Task Unlinked_state_resets_can_edit_preferences()
    {
        var harness = new ScreenHarness();
        var viewModel = harness.Notifications();
        await viewModel.LoadAsync();
        Assert.True(viewModel.CanEditPreferences);

        harness.State.IsLinked = false;
        await viewModel.LoadAsync();

        Assert.False(viewModel.CanEditPreferences);
        Assert.Empty(viewModel.Items);
    }

    [Fact]
    public async Task Load_more_is_skipped_while_loading_or_refreshing()
    {
        var harness = new ScreenHarness();
        harness.Api.NotificationPages.Add(new NotificationListResponse(
            [TestNotifications.Notification()],
            "cursor-1"));
        var viewModel = harness.Notifications();
        await viewModel.LoadAsync();

        viewModel.IsRefreshing = true;
        await viewModel.LoadMoreCommand.ExecuteAsync(null);
        viewModel.IsRefreshing = false;

        Assert.Single(harness.Api.NotificationRequests);
    }
}
