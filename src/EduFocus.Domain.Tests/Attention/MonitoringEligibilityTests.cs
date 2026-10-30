using EduFocus.Domain.Attention;
using EduFocus.Domain.Entities;
using EduFocus.Domain.Enums;
using FluentAssertions;

namespace EduFocus.Domain.Tests.Attention;

public class MonitoringEligibilityTests
{
    private static readonly DateTime Now = new(2026, 10, 30, 8, 0, 0, DateTimeKind.Utc);

    [Theory]
    [InlineData(false, false, false, false)]
    [InlineData(false, true, true, false)]
    [InlineData(true, false, false, true)]
    [InlineData(true, false, true, true)]
    [InlineData(true, true, false, false)]
    [InlineData(true, true, true, true)]
    public void Eligibility_follows_monitoring_settings_and_class_state(
        bool monitoringEnabled,
        bool monitorOnlyDuringClasses,
        bool isOnlineClass,
        bool expected)
    {
        MonitoringEligibility
            .IsEligible(monitoringEnabled, monitorOnlyDuringClasses, isOnlineClass)
            .Should()
            .Be(expected);
    }

    [Fact]
    public void Class_only_monitoring_changes_the_coverage_denominator()
    {
        (int Seconds, bool IsOnlineClass)[] windows =
        [
            (50, true),
            (50, false),
            (100, false)
        ];

        var alwaysEligible = windows
            .Where(window => MonitoringEligibility.IsEligible(true, false, window.IsOnlineClass))
            .Sum(window => window.Seconds);
        var classOnlyEligible = windows
            .Where(window => MonitoringEligibility.IsEligible(true, true, window.IsOnlineClass))
            .Sum(window => window.Seconds);

        alwaysEligible.Should().Be(200);
        classOnlyEligible.Should().Be(50);

        var alwaysOn = SessionWithCoverage(monitoredSeconds: 40, alwaysEligible);
        var classOnly = SessionWithCoverage(monitoredSeconds: 40, classOnlyEligible);

        alwaysOn.CoveragePercent.Should().Be(20);
        classOnly.CoveragePercent.Should().Be(80);
    }

    [Fact]
    public void No_eligible_time_makes_coverage_unknown()
    {
        var session = SessionWithCoverage(monitoredSeconds: 0, eligibleSeconds: 0);

        session.CoveragePercent.Should().BeNull();
    }

    private static StudySession SessionWithCoverage(int monitoredSeconds, int eligibleSeconds)
    {
        var session = StudySession.Start(
            Now,
            new DateOnly(2026, 10, 30),
            SessionTrigger.Manual,
            monitoringEnabled: true);

        if (monitoredSeconds > 0)
            session.AddAttentionSeconds(AttentionState.Focused, monitoredSeconds, Now.AddSeconds(monitoredSeconds));

        if (eligibleSeconds > 0)
            session.AddMonitoringEligibleSeconds(eligibleSeconds, Now.AddSeconds(eligibleSeconds));

        return session;
    }
}
