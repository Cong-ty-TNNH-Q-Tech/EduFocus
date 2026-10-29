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
    public async Task Beat_caps_a_long_suspended_gap()
    {
        await using var harness = new HeartbeatHarness();
        await harness.StartAsync();

        harness.Monotonic.Ticks += TimeSpan.FromMinutes(10);
        harness.Clock.Advance(TimeSpan.FromMinutes(10));

        await harness.Service.BeatOnceAsync();

        harness.Session.ActiveSeconds.Should().Be(60);
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
        public HeartbeatHarness(TimeSpan? interval = null)
        {
            Session = StudySession.Start(
                Now,
                new DateOnly(2026, 10, 30),
                SessionTrigger.Manual,
                monitoringEnabled: true);

            Sessions.Sessions.Add(Session);
            Service = new StudySessionHeartbeatService(
                Sessions,
                Monotonic,
                Logger,
                Clock,
                new StudySessionHeartbeatOptions
                {
                    Interval = interval ?? TimeSpan.FromSeconds(10)
                });
        }

        public ManualTimeProvider Clock { get; } = new(new DateTimeOffset(Now), TimeZoneInfo.Utc);

        public FakeMonotonicClock Monotonic { get; } = new();

        public FakeStudySessionRepository Sessions { get; } = new();

        public RecordingLogger Logger { get; } = new();

        public StudySession Session { get; }

        public StudySessionHeartbeatService Service { get; }

        public Task StartAsync() => Service.StartAsync(Session.Id, Session.LastHeartbeatAtUtc);

        public ValueTask DisposeAsync() => Service.DisposeAsync();
    }
}
