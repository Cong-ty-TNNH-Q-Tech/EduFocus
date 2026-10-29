using EduFocus.Domain.Reports;
using EduFocus.UseCases.Reports;
using EduFocus.UseCases.Tests.TestSupport;
using FluentAssertions;

namespace EduFocus.UseCases.Tests.Reports;

public sealed class DashboardServiceTests
{
    [Fact]
    public async Task Today_uses_the_local_calendar_date_of_the_time_provider()
    {
        var repository = new FakeStudySessionRepository();
        var service = new DashboardService(
            repository,
            new ManualTimeProvider(
                new DateTimeOffset(2026, 10, 29, 18, 30, 0, TimeSpan.Zero),
                UtcPlus7()));

        await service.GetTodayAsync();

        repository.DailySummaryRequests.Should().ContainSingle()
            .Which.Should().Be(new DateOnly(2026, 10, 30));
    }

    [Fact]
    public async Task Empty_day_returns_an_empty_summary_instead_of_null()
    {
        var repository = new FakeStudySessionRepository();
        var service = new DashboardService(
            repository,
            new ManualTimeProvider(new DateTimeOffset(2026, 10, 30, 9, 0, 0, TimeSpan.Zero), UtcPlus7()));

        var summary = await service.GetTodayAsync();

        summary.LocalDate.Should().Be(new DateOnly(2026, 10, 30));
        summary.HasSessions.Should().BeFalse();
        summary.SessionCount.Should().Be(0);
        summary.Attention.AttentionPercent.Should().BeNull();
        summary.Attention.Warning.Should().BeNull();
    }

    [Fact]
    public async Task Repository_summary_is_returned_unchanged()
    {
        var repository = new FakeStudySessionRepository
        {
            DailySummary = new DailyStudySummary
            {
                LocalDate = new DateOnly(2026, 10, 30),
                SessionCount = 2,
                StudySeconds = 3_600,
                FocusedSeconds = 600,
                MonitoringEligibleSeconds = 1_000
            }
        };
        var service = new DashboardService(
            repository,
            new ManualTimeProvider(new DateTimeOffset(2026, 10, 30, 9, 0, 0, TimeSpan.Zero), UtcPlus7()));

        var summary = await service.GetTodayAsync();

        summary.Should().BeSameAs(repository.DailySummary);
        summary.Attention.Warning.Should().BeNull();
    }

    [Fact]
    public async Task Repository_failure_propagates_to_the_view_model()
    {
        var repository = new FakeStudySessionRepository { ThrowOnDailySummary = true };
        var service = new DashboardService(
            repository,
            new ManualTimeProvider(new DateTimeOffset(2026, 10, 30, 9, 0, 0, TimeSpan.Zero), UtcPlus7()));

        var act = () => service.GetTodayAsync();

        await act.Should().ThrowAsync<InvalidOperationException>();
    }

    private static TimeZoneInfo UtcPlus7() =>
        TimeZoneInfo.CreateCustomTimeZone(
            "EduFocus/Test-UTC7",
            TimeSpan.FromHours(7),
            "EduFocus Test UTC+7",
            "EduFocus Test UTC+7");
}
