using EduFocus.Parent.Core.Presentation;
using EduFocus.Parent.Core.Services;
using EduFocus.Parent.Tests.TestSupport;

namespace EduFocus.Parent.Tests.Presentation;

public sealed class OverviewViewModelTests
{
    [Fact]
    public async Task Loads_today_summary_and_formats_values()
    {
        var harness = new ScreenHarness();
        harness.Api.DailyResult = [TestSummaries.Daily(ScreenHarness.Today)];

        var viewModel = harness.Overview();
        await viewModel.LoadAsync();

        Assert.Equal((ScreenHarness.Today, ScreenHarness.Today), Assert.Single(harness.Api.DailyRequests));
        Assert.True(viewModel.HasSummary);
        Assert.False(viewModel.ShowEmptyState);
        Assert.False(viewModel.HasError);
        Assert.Equal("2 giờ 15 phút", viewModel.StudyTimeText);
        Assert.Equal("2 giờ 30 phút", viewModel.ActiveTimeText);
        Assert.Equal("30 phút", viewModel.OnlineClassTimeText);
        Assert.Equal("3", viewModel.SessionsText);
        Assert.Equal("2", viewModel.BlockedText);
        Assert.Equal("82,5%", viewModel.AttentionText);
        Assert.Equal("96,0%", viewModel.CoverageText);
        Assert.False(viewModel.ShowCoverageWarning);
        Assert.Equal("Cập nhật lúc 17:30 17/11/2026", viewModel.LastUpdatedText);
        Assert.StartsWith("Hôm nay, Thứ Ba", viewModel.TodayText);
        Assert.False(viewModel.IsShowingCachedData);
    }

    [Fact]
    public async Task Null_attention_and_coverage_show_a_dash()
    {
        var harness = new ScreenHarness();
        harness.Api.DailyResult = [TestSummaries.Daily(ScreenHarness.Today, attention: null, coverage: null)];

        var viewModel = harness.Overview();
        await viewModel.LoadAsync();

        Assert.Equal("—", viewModel.AttentionText);
        Assert.Equal("—", viewModel.CoverageText);
        Assert.False(viewModel.ShowCoverageWarning);
    }

    [Fact]
    public async Task Low_coverage_shows_warning()
    {
        var harness = new ScreenHarness();
        harness.Api.DailyResult = [TestSummaries.Daily(ScreenHarness.Today, coverage: 42.4)];

        var viewModel = harness.Overview();
        await viewModel.LoadAsync();

        Assert.True(viewModel.ShowCoverageWarning);
        Assert.Equal("42,4%", viewModel.CoverageText);
    }

    [Fact]
    public async Task Empty_response_shows_empty_state()
    {
        var harness = new ScreenHarness();

        var viewModel = harness.Overview();
        await viewModel.LoadAsync();

        Assert.False(viewModel.HasSummary);
        Assert.True(viewModel.ShowEmptyState);
        Assert.Equal("—", viewModel.StudyTimeText);
    }

    [Fact]
    public async Task Network_error_falls_back_to_cached_today()
    {
        var harness = new ScreenHarness();
        harness.Cache.SaveDaily([TestSummaries.Daily(ScreenHarness.Today, studySeconds: 5400)]);
        harness.Api.DailyError = new HttpRequestException("offline");

        var viewModel = harness.Overview();
        await viewModel.LoadAsync();

        Assert.True(viewModel.HasError);
        Assert.True(viewModel.IsShowingCachedData);
        Assert.True(viewModel.HasSummary);
        Assert.Equal("1 giờ 30 phút", viewModel.StudyTimeText);
        Assert.Contains("10:00 17/11/2026", viewModel.CachedBannerText);
    }

    [Fact]
    public async Task Unauthorized_clears_the_link_and_raises_expiry()
    {
        var harness = new ScreenHarness();
        harness.Cache.SaveDaily([TestSummaries.Daily(ScreenHarness.Today)]);
        harness.Api.DailyError = new CloudApiException(401, "invalid_token", "Mã truy cập không hợp lệ.");
        var raised = 0;
        harness.Session.Expired += () => raised++;

        var viewModel = harness.Overview();
        await viewModel.LoadAsync();

        Assert.Equal(1, raised);
        Assert.False(harness.State.IsLinked);
        Assert.Null(harness.Tokens.Token);
        Assert.Null(harness.Settings.FamilyId);
        Assert.False(harness.Cache.TryGetDaily(out _, out _));
        Assert.Equal(CloudErrors.SessionExpired, viewModel.ErrorMessage);
    }

    [Fact]
    public async Task Unlinked_device_does_not_call_the_cloud()
    {
        var harness = new ScreenHarness();
        harness.State.IsLinked = false;

        var viewModel = harness.Overview();
        await viewModel.LoadAsync();

        Assert.Empty(harness.Api.DailyRequests);
        Assert.True(viewModel.ShowEmptyState);
        Assert.False(viewModel.HasError);
    }

    [Fact]
    public async Task Refresh_command_reloads_data()
    {
        var harness = new ScreenHarness();
        harness.Api.DailyResult = [TestSummaries.Daily(ScreenHarness.Today)];

        var viewModel = harness.Overview();
        await viewModel.RefreshCommand.ExecuteAsync(null);

        Assert.Single(harness.Api.DailyRequests);
        Assert.True(viewModel.HasSummary);
        Assert.False(viewModel.IsRefreshing);
    }
}
