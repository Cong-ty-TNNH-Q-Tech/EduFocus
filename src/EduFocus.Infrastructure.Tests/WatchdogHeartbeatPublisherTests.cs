using System.Diagnostics;
using EduFocus.Infrastructure.Watchdog;
using EduFocus.Ports.DTOs;
using EduFocus.Ports.Outbound.Services;
using FluentAssertions;

namespace EduFocus.Infrastructure.Tests;

public sealed class WatchdogHeartbeatPublisherTests
{
    [Fact]
    public async Task The_first_beat_identifies_the_current_process()
    {
        var store = new RecordingHeartbeatStore();
        var publisher = CreatePublisher(store);

        publisher.Start();
        await store.FirstWrite.Task.WaitAsync(TimeSpan.FromSeconds(5));

        var heartbeat = store.Writes[0];
        heartbeat.ProcessId.Should().Be(Environment.ProcessId);
        using var process = Process.GetCurrentProcess();
        heartbeat.ProcessStartedAtUtc.Should().BeCloseTo(
            process.StartTime.ToUniversalTime(),
            TimeSpan.FromSeconds(2));
        heartbeat.UpdatedAtUtc.Kind.Should().Be(DateTimeKind.Utc);

        await publisher.DisposeAsync();
    }

    [Fact]
    public async Task Disposing_removes_the_heartbeat_and_stops_writing()
    {
        var store = new RecordingHeartbeatStore();
        var publisher = CreatePublisher(store);

        publisher.Start();
        await store.FirstWrite.Task.WaitAsync(TimeSpan.FromSeconds(5));
        await publisher.DisposeAsync();

        store.Deletes.Should().Be(1);
        var writes = store.WriteCount;
        await Task.Delay(TimeSpan.FromMilliseconds(1200));
        store.WriteCount.Should().Be(writes);
    }

    [Fact]
    public void An_interval_below_one_second_is_rejected()
    {
        var act = () => new WatchdogHeartbeatPublisher(
            new RecordingHeartbeatStore(),
            new WatchdogHeartbeatOptions { Interval = TimeSpan.FromMilliseconds(500) },
            new TestLogger(),
            TimeProvider.System);

        act.Should().Throw<ArgumentOutOfRangeException>();
    }

    private static WatchdogHeartbeatPublisher CreatePublisher(IWatchdogHeartbeatStore store) =>
        new(
            store,
            new WatchdogHeartbeatOptions { Interval = TimeSpan.FromSeconds(1) },
            new TestLogger(),
            TimeProvider.System);

    private sealed class RecordingHeartbeatStore : IWatchdogHeartbeatStore
    {
        private readonly object _gate = new();

        public List<WatchdogHeartbeat> Writes { get; } = [];

        public int Deletes { get; private set; }

        public TaskCompletionSource FirstWrite { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public int WriteCount
        {
            get
            {
                lock (_gate)
                    return Writes.Count;
            }
        }

        public Task<WatchdogHeartbeat?> ReadAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult<WatchdogHeartbeat?>(null);

        public Task WriteAsync(WatchdogHeartbeat heartbeat, CancellationToken cancellationToken = default)
        {
            lock (_gate)
                Writes.Add(heartbeat);

            FirstWrite.TrySetResult();
            return Task.CompletedTask;
        }

        public Task DeleteAsync(CancellationToken cancellationToken = default)
        {
            lock (_gate)
                Deletes++;

            return Task.CompletedTask;
        }
    }
}
