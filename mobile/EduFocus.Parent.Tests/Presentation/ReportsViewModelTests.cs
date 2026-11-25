using EduFocus.Parent.Core.Services;
using EduFocus.Parent.Tests.TestSupport;

namespace EduFocus.Parent.Tests.Presentation;

public sealed class ReportsViewModelTests
{
    [Fact]
    public async Task Requests_seven_days_and_orders_days_and_chart()
    {
        var harness = new ScreenHarness();
        harness.Api.DailyResult =
        [
            TestSummaries.Daily(ScreenHarness.Today),
            TestSummaries.Daily(ScreenHarness.Today.AddDays(-6)),
            TestSummaries.Daily(ScreenHarness.Today.AddDays(-3)),
        ];

        var viewModel = harness.Reports();
        await viewModel.LoadAsync();

        var request = Assert.Single(harness.Api.DailyRequests);
        Assert.Equal(ScreenHarness.Today.AddDays(-6), request.From);
        Assert.Equal(ScreenHarness.Today, request.To);
        Assert.Equal(3, viewModel.Days.Count);
        Assert.Equal(ScreenHarness.Today, viewModel.Days[0].Date);
        Assert.True(viewModel.Days[0].IsToday);
        Assert.Equal(3, viewModel.ChartBars.Count);
        Assert.Equal("11/11", viewModel.ChartBars[0].Label);
        Assert.True(viewModel.ChartBars[^1].IsToday);
        Assert.True(viewModel.HasDays);
        Assert.True(viewModel.HasSelection);
        Assert.Contains("17/11", viewModel.SelectedDateText);
        Assert.Equal("2 giờ 15 phút", viewModel.SelectedStudyText);
        Assert.Equal("6 giờ 45 phút", viewModel.TotalStudyText);
        Assert.Equal("2 giờ 15 phút", viewModel.AverageStudyText);
        Assert.Equal(0.825, viewModel.SelectedAttentionPercentValue, 6);
        Assert.Equal(0.96, viewModel.SelectedCoveragePercentValue, 6);
    }

    [Fact]
    public async Task Focus_date_from_push_selects_that_day()
    {
        var harness = new ScreenHarness();
        var focus = ScreenHarness.Today.AddDays(-5);
        harness.Api.DailyResult =
        [
            TestSummaries.Daily(ScreenHarness.Today),
            TestSummaries.Daily(focus, studySeconds: 3600),
        ];

        var viewModel = harness.Reports();
        await viewModel.LoadAsync(focus);

        Assert.True(viewModel.HasSelection);
        Assert.Contains("12/11", viewModel.SelectedDateText);
        Assert.Equal("1 giờ", viewModel.SelectedStudyText);
    }

    [Fact]
    public async Task Selected_day_exposes_coverage_warning()
    {
        var harness = new ScreenHarness();
        harness.Api.DailyResult = [TestSummaries.Daily(ScreenHarness.Today, coverage: 38)];

        var viewModel = harness.Reports();
        await viewModel.LoadAsync();

        Assert.True(viewModel.SelectedShowCoverageWarning);
        Assert.Equal("38,0%", viewModel.SelectedCoverageText);
    }

    [Fact]
    public async Task Select_command_updates_details()
    {
        var harness = new ScreenHarness();
        harness.Api.DailyResult =
        [
            TestSummaries.Daily(ScreenHarness.Today),
            TestSummaries.Daily(ScreenHarness.Today.AddDays(-1), studySeconds: 1800),
        ];

        var viewModel = harness.Reports();
        await viewModel.LoadAsync();
        viewModel.SelectCommand.Execute(viewModel.Days[1]);

        Assert.Equal("30 phút", viewModel.SelectedStudyText);
        Assert.Contains("16/11", viewModel.SelectedDateText);
    }

    [Fact]
    public async Task Error_falls_back_to_cached_days()
    {
        var harness = new ScreenHarness();
        harness.Cache.SaveDaily([TestSummaries.Daily(ScreenHarness.Today, studySeconds: 1800)]);
        harness.Api.DailyError = new HttpRequestException("offline");

        var viewModel = harness.Reports();
        await viewModel.LoadAsync();

        Assert.True(viewModel.HasError);
        Assert.True(viewModel.IsShowingCachedData);
        Assert.True(viewModel.HasDays);
        Assert.Equal("30 phút", viewModel.SelectedStudyText);
        Assert.NotEmpty(viewModel.CachedBannerText);
    }

    [Fact]
    public async Task Empty_range_shows_empty_state()
    {
        var harness = new ScreenHarness();

        var viewModel = harness.Reports();
        await viewModel.LoadAsync();

        Assert.False(viewModel.HasDays);
        Assert.True(viewModel.ShowEmptyState);
        Assert.False(viewModel.HasSelection);
    }

    [Fact]
    public async Task Unauthorized_clears_link()
    {
        var harness = new ScreenHarness();
        harness.Api.DailyError = new CloudApiException(401, "invalid_token", "Hết phiên.");
        var raised = 0;
        harness.Session.Expired += () => raised++;

        var viewModel = harness.Reports();
        await viewModel.LoadAsync();

        Assert.Equal(1, raised);
        Assert.False(harness.State.IsLinked);
        Assert.True(viewModel.HasError);
    }
}
