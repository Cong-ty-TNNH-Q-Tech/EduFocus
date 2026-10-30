using System.Diagnostics;
using EduFocus.Domain.Entities;
using EduFocus.Domain.Enums;
using EduFocus.UseCases.Sessions;
using EduFocus.UseCases.Tests.TestSupport;
using FluentAssertions;

namespace EduFocus.UseCases.Tests.Sessions;

public sealed class StudySessionHeartbeatServiceTests
{
    private static readonly DateTime Now = new(2026, 10, 30, 8, 0, 0, DateTimeKind.Utc);

    [Fact]
    public async Task Beat_writes_heartbeat_and_active_seconds_from_monotonic_clock()
    {
        await using var harness = new HeartbeatHarness();
        await harness.StartAsync();

        harness.Clock.Advance(TimeSpan.FromSeconds(10));
        harness.Monotonic.Ticks += TimeSpan.FromSeconds(10);

        var written = await harness.Service.BeatOnceAsync();

        written.Should().BeTrue();
        harness.Session.LastHeartbeatAtUtc.Should().Be(Now.AddSeconds(10));
        harness.Session.ActiveSeconds.Should().Be(10);
        harness.Sessions.Heartbeats.Should().ContainSingle()
            .Which.ActiveSeconds.Should().Be(10);
    }

    [Fact]
    public async Task Beat_does_not_inflate_active_seconds_after_a_wall_clock_forward_jump()
    {
        await using var harness = new HeartbeatHarness();
        await harness.StartAsync();

        harness.Monotonic.Ticks += TimeSpan.FromSeconds(10);
        harness.Clock.Advance(TimeSpan.FromHours(2));

        await harness.Service.BeatOnceAsync();

        harness.Session.ActiveSeconds.Should().Be(10);
        harness.Session.LastHeartbeatAtUtc.Should().Be(Now.AddHours(2));
    }

    [Fact]
    public async Task Beat_with_a_backward_wall_clock_keeps_the_heartbeat_monotonic()
    {
        await using var harness = new HeartbeatHarness();
        await harness.StartAsync();

        harness.Monotonic.Ticks += TimeSpan.FromSeconds(10);
        harness.Clock.Advance(TimeSpan.FromSeconds(10));
        await harness.Service.BeatOnceAsync();

        harness.Clock.Advance(TimeSpan.FromMinutes(-5));
        harness.Monotonic.Ticks += TimeSpan.FromSeconds(10);

        await harness.Service.BeatOnceAsync();

        harness.Session.ActiveSeconds.Should().Be(20);
        harness.Session.LastHeartbeatAtUtc.Should().Be(Now.AddSeconds(10));
        harness.Session.UpdatedAtUtc.Should().BeOnOrAfter(Now.AddSeconds(10));
    }

    [Fact]
    public async Task Long_gap_without_power_events_is_not_counted()
    {
        await using var harness = new HeartbeatHarness();
        await harness.StartAsync();

        harness.Monotonic.Ticks += TimeSpan.FromMinutes(10);
        harness.Clock.Advance(TimeSpan.FromMinutes(10));

        await harness.Service.BeatOnceAsync();

        harness.Session.ActiveSeconds.Should().Be(0);

        harness.Monotonic.Ticks += TimeSpan.FromSeconds(10);
        harness.Clock.Advance(TimeSpan.FromSeconds(10));

        await harness.Service.BeatOnceAsync();

        harness.Session.ActiveSeconds.Should().Be(10);
    }

    [Fact]
    public async Task Suspend_and_resume_exclude_sleep_from_active_seconds()
    {
        await using var harness = new HeartbeatHarness();
        await harness.StartAsync();

        harness.Monotonic.Ticks += TimeSpan.FromSeconds(5);
        harness.Power.RaiseSuspended();

        harness.Monotonic.Ticks += TimeSpan.FromMinutes(10);
        harness.Clock.Advance(TimeSpan.FromMinutes(10));
        harness.Power.RaiseResumed();

        (await harness.Service.BeatOnceAsync()).Should().BeTrue();

        harness.Session.ActiveSeconds.Should().Be(5);
        harness.Session.LastHeartbeatAtUtc.Should().Be(Now.AddMinutes(10));

        harness.Monotonic.Ticks += TimeSpan.FromSeconds(10);
        harness.Clock.Advance(TimeSpan.FromSeconds(10));

        await harness.Service.BeatOnceAsync();

        harness.Session.ActiveSeconds.Should().Be(15);
    }

    [Fact]
    public async Task Stopping_while_suspended_writes_only_the_pre_suspend_slice()
    {
        await using var harness = new HeartbeatHarness();
        await harness.StartAsync();

        harness.Monotonic.Ticks += TimeSpan.FromSeconds(5);
        harness.Power.RaiseSuspended();
        harness.Monotonic.Ticks += TimeSpan.FromMinutes(10);
        harness.Clock.Advance(TimeSpan.FromMinutes(10));

        await harness.Service.StopAsync();

        harness.Session.ActiveSeconds.Should().Be(5);
        harness.Sessions.Heartbeats.Should().ContainSingle()
            .Which.ActiveSeconds.Should().Be(5);
    }

    [Fact]
    public async Task Repeated_suspend_events_keep_the_earliest_boundary()
    {
        await using var harness = new HeartbeatHarness();
        await harness.StartAsync();

        harness.Monotonic.Ticks += TimeSpan.FromSeconds(5);
        harness.Power.RaiseSuspended();

        harness.Monotonic.Ticks += TimeSpan.FromSeconds(10);
        harness.Power.RaiseSuspended();

        harness.Monotonic.Ticks += TimeSpan.FromMinutes(10);
        harness.Clock.Advance(TimeSpan.FromMinutes(10));
        harness.Power.RaiseResumed();

        await harness.Service.BeatOnceAsync();

        harness.Session.ActiveSeconds.Should().Be(5);
    }

    [Fact]
    public async Task Multiple_suspend_cycles_before_a_beat_add_only_active_slices()
    {
        await using var harness = new HeartbeatHarness();
        await harness.StartAsync();

        harness.Monotonic.Ticks += TimeSpan.FromSeconds(5);
        harness.Power.RaiseSuspended();
        harness.Monotonic.Ticks += TimeSpan.FromMinutes(10);
        harness.Clock.Advance(TimeSpan.FromMinutes(10));
        harness.Power.RaiseResumed();

        harness.Monotonic.Ticks += TimeSpan.FromSeconds(5);
        harness.Power.RaiseSuspended();
        harness.Monotonic.Ticks += TimeSpan.FromMinutes(10);
        harness.Clock.Advance(TimeSpan.FromMinutes(10));
        harness.Power.RaiseResumed();

        await harness.Service.BeatOnceAsync();

        harness.Session.ActiveSeconds.Should().Be(10);
    }

    [Fact]
    public async Task Suspended_beats_keep_waiting_for_resume_without_counting_locked_time()
    {
        await using var harness = new HeartbeatHarness();
        await harness.StartAsync();

        harness.Monotonic.Ticks += TimeSpan.FromSeconds(5);
        harness.Power.RaiseSuspended();

        for (var i = 0; i < 12; i++)
        {
            harness.Monotonic.Ticks += TimeSpan.FromSeconds(10);
            harness.Clock.Advance(TimeSpan.FromSeconds(10));
            await harness.Service.BeatOnceAsync();
        }

        harness.Session.ActiveSeconds.Should().Be(5);

        harness.Power.RaiseResumed();
        harness.Monotonic.Ticks += TimeSpan.FromSeconds(10);
        harness.Clock.Advance(TimeSpan.FromSeconds(10));
        await harness.Service.BeatOnceAsync();

        harness.Session.ActiveSeconds.Should().Be(15);
    }

    [Fact]
    public async Task Missing_resume_event_after_a_long_gap_restarts_counting()
    {
        await using var harness = new HeartbeatHarness();
        await harness.StartAsync();

        harness.Monotonic.Ticks += TimeSpan.FromSeconds(5);
        harness.Power.RaiseSuspended();

        harness.Monotonic.Ticks += TimeSpan.FromMinutes(10);
        harness.Clock.Advance(TimeSpan.FromMinutes(10));

        await harness.Service.BeatOnceAsync();

        harness.Session.ActiveSeconds.Should().Be(5);

        harness.Monotonic.Ticks += TimeSpan.FromSeconds(10);
        harness.Clock.Advance(TimeSpan.FromSeconds(10));
        await harness.Service.BeatOnceAsync();

        harness.Session.ActiveSeconds.Should().Be(15);
    }

    [Fact]
    public async Task Dst_wall_clock_transition_does_not_change_active_seconds()
    {
        await using var harness = new HeartbeatHarness(
            timeZone: TimeZoneInfo.FindSystemTimeZoneById("Pacific Standard Time"));
        await harness.StartAsync();

        harness.Monotonic.Ticks += TimeSpan.FromSeconds(10);
        harness.Clock.Advance(TimeSpan.FromDays(120));

        await harness.Service.BeatOnceAsync();

        harness.Session.ActiveSeconds.Should().Be(10);
        harness.Session.LastHeartbeatAtUtc.Should().Be(Now.AddDays(120));
    }

    [Fact]
    public async Task Beat_reports_that_the_session_has_ended()
    {
        await using var harness = new HeartbeatHarness();
        await harness.StartAsync();
        harness.Session.Complete(Now.AddMinutes(1));

        var written = await harness.Service.BeatOnceAsync();

        written.Should().BeFalse();
        harness.Sessions.Heartbeats.Should().BeEmpty();
        harness.Logger.Warnings.Should().Contain(message =>
            message.Contains("no longer active", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Database_failure_is_logged_and_the_next_beat_retries()
    {
        await using var harness = new HeartbeatHarness();
        await harness.StartAsync();
        harness.Sessions.ThrowOnHeartbeat = true;

        harness.Monotonic.Ticks += TimeSpan.FromSeconds(10);
        harness.Clock.Advance(TimeSpan.FromSeconds(10));

        (await harness.Service.BeatOnceAsync()).Should().BeTrue();
        harness.Session.ActiveSeconds.Should().Be(0);
        harness.Logger.Warnings.Should().Contain(message =>
            message.Contains("next beat will retry", StringComparison.Ordinal));

        harness.Sessions.ThrowOnHeartbeat = false;
        harness.Monotonic.Ticks += TimeSpan.FromSeconds(10);
        harness.Clock.Advance(TimeSpan.FromSeconds(10));

        (await harness.Service.BeatOnceAsync()).Should().BeTrue();

        harness.Session.ActiveSeconds.Should().Be(20);
        harness.Sessions.Heartbeats.Should().ContainSingle()
            .Which.ActiveSeconds.Should().Be(20);
    }

    [Fact]
    public async Task Repeated_start_stop_cycles_accumulate_without_losing_active_time()
    {
        await using var harness = new HeartbeatHarness();

        await harness.StartAsync();
        harness.Monotonic.Ticks += TimeSpan.FromSeconds(5);
        harness.Clock.Advance(TimeSpan.FromSeconds(5));
        await harness.Service.StopAsync();

        harness.Service.IsRunning.Should().BeFalse();
        harness.Session.ActiveSeconds.Should().Be(5);

        await harness.Service.StartAsync(harness.Session.Id, harness.Session.LastHeartbeatAtUtc);
        harness.Monotonic.Ticks += TimeSpan.FromSeconds(5);
        harness.Clock.Advance(TimeSpan.FromSeconds(5));
        await harness.Service.StopAsync();

        harness.Session.ActiveSeconds.Should().Be(10);
        harness.Sessions.Heartbeats.Should().HaveCount(2);
        harness.Service.IsRunning.Should().BeFalse();
    }

    [Fact]
    public async Task Background_loop_writes_cadenced_beats_and_stops_on_demand()
    {
        await using var harness = new HeartbeatHarness(interval: TimeSpan.FromMilliseconds(25));
        await harness.StartAsync();

        var deadline = Stopwatch.StartNew();
        while (harness.Sessions.Heartbeats.Count < 2 && deadline.Elapsed < TimeSpan.FromSeconds(3))
            await Task.Delay(10);

        harness.Sessions.Heartbeats.Should().HaveCountGreaterThanOrEqualTo(2);
        harness.Service.IsRunning.Should().BeTrue();

        await harness.Service.StopAsync();
        var afterStop = harness.Sessions.Heartbeats.Count;

        await Task.Delay(150);

        harness.Sessions.Heartbeats.Should().HaveCount(afterStop);
        harness.Service.IsRunning.Should().BeFalse();
    }

    [Fact]
    public async Task Beat_before_start_returns_false()
    {
        await using var harness = new HeartbeatHarness();

        (await harness.Service.BeatOnceAsync()).Should().BeFalse();
        harness.Sessions.Heartbeats.Should().BeEmpty();
    }

    private sealed class HeartbeatHarness : IAsyncDisposable
    {
        public HeartbeatHarness(TimeSpan? interval = null, TimeZoneInfo? timeZone = null)
        {
            Clock = new ManualTimeProvider(new DateTimeOffset(Now), timeZone ?? TimeZoneInfo.Utc);

            Session = StudySession.Start(
                Now,
                new DateOnly(2026, 10, 30),
                SessionTrigger.Manual,
                monitoringEnabled: true);

            Sessions.Sessions.Add(Session);
            Service = new StudySessionHeartbeatService(
                Sessions,
                Monotonic,
                Power,
                Logger,
                Clock,
                new StudySessionHeartbeatOptions
                {
                    Interval = interval ?? TimeSpan.FromSeconds(10)
                });
        }

        public ManualTimeProvider Clock { get; }

        public FakeMonotonicClock Monotonic { get; } = new();

        public FakeSystemPowerEventPort Power { get; } = new();

        public FakeStudySessionRepository Sessions { get; } = new();

        public RecordingLogger Logger { get; } = new();

        public StudySession Session { get; }

        public StudySessionHeartbeatService Service { get; }

        public Task StartAsync() => Service.StartAsync(Session.Id, Session.LastHeartbeatAtUtc);

        public ValueTask DisposeAsync() => Service.DisposeAsync();
    }
}
