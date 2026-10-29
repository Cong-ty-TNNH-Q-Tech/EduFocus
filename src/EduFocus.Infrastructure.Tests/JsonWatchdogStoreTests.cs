using EduFocus.Infrastructure.Watchdog;
using EduFocus.Ports.DTOs;
using EduFocus.Ports.Outbound.Services;
using FluentAssertions;

namespace EduFocus.Infrastructure.Tests;

public sealed class JsonWatchdogStoreTests : IDisposable
{
    private readonly string _root;
    private readonly WatchdogRuntimeFiles _files;
    private readonly TestLogger _logger = new();

    public JsonWatchdogStoreTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "edufocus-watchdog-json", Guid.NewGuid().ToString("N"));
        _files = new WatchdogRuntimeFiles(new WatchdogRuntimeOptions { RuntimeDirectory = _root });
    }

    [Fact]
    public async Task Heartbeat_round_trips_pid_start_time_and_timestamp()
    {
        var store = new JsonWatchdogHeartbeatStore(_files, _logger);
        var heartbeat = new WatchdogHeartbeat(
            4242,
            new DateTime(2026, 10, 29, 7, 0, 0, DateTimeKind.Utc),
            new DateTime(2026, 10, 29, 8, 0, 0, DateTimeKind.Utc));

        await store.WriteAsync(heartbeat);
        var read = await store.ReadAsync();

        read.Should().Be(heartbeat);
    }

    [Fact]
    public async Task Missing_heartbeat_reads_as_null()
    {
        var store = new JsonWatchdogHeartbeatStore(_files, _logger);

        (await store.ReadAsync()).Should().BeNull();
        _logger.Warnings.Should().BeEmpty();
    }

    [Fact]
    public async Task Unreadable_heartbeat_is_ignored_with_a_warning()
    {
        _files.WriteAtomic(_files.HeartbeatPath, "{ broken");
        var store = new JsonWatchdogHeartbeatStore(_files, _logger);

        (await store.ReadAsync()).Should().BeNull();
        _logger.Warnings.Should().ContainSingle();
    }

    [Fact]
    public async Task Heartbeat_delete_removes_the_file()
    {
        var store = new JsonWatchdogHeartbeatStore(_files, _logger);
        await store.WriteAsync(new WatchdogHeartbeat(1, DateTime.UtcNow, DateTime.UtcNow));

        await store.DeleteAsync();

        File.Exists(_files.HeartbeatPath).Should().BeFalse();
    }

    [Fact]
    public async Task Restart_state_round_trips_every_field()
    {
        var store = new JsonWatchdogStateStore(_files, _logger);
        var state = new WatchdogRestartState
        {
            RestartCount = 3,
            WindowStartedAtUtc = new DateTime(2026, 10, 29, 7, 50, 0, DateTimeKind.Utc),
            LastRestartAtUtc = new DateTime(2026, 10, 29, 7, 59, 0, DateTimeKind.Utc),
            NextRestartAtUtc = new DateTime(2026, 10, 29, 8, 0, 20, DateTimeKind.Utc),
            GaveUpUntilUtc = null,
            GaveUpRecordedAtUtc = null,
            PendingProcessId = 4321,
            PendingProcessStartedAtUtc = new DateTime(2026, 10, 29, 7, 59, 5, DateTimeKind.Utc),
            PendingReason = WatchdogRestartReason.UnexpectedExit
        };

        await store.WriteAsync(state);
        var read = await store.ReadAsync();

        read.Should().Be(state);
    }

    [Fact]
    public async Task Missing_state_reads_as_null()
    {
        var store = new JsonWatchdogStateStore(_files, _logger);

        (await store.ReadAsync()).Should().BeNull();
    }

    [Fact]
    public async Task Unreadable_state_is_ignored_with_a_warning()
    {
        _files.WriteAtomic(_files.StatePath, "[]");
        var store = new JsonWatchdogStateStore(_files, _logger);

        (await store.ReadAsync()).Should().BeNull();
        _logger.Warnings.Should().ContainSingle();
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_root))
                Directory.Delete(_root, recursive: true);
        }
        catch (IOException)
        {
            // Test cleanup is best effort.
        }
    }

    private sealed class TestLogger : IAppLogger
    {
        public List<string> Warnings { get; } = [];

        public List<string> Errors { get; } = [];

        public void Info(string category, string message)
        {
        }

        public void Warning(string category, string message) => Warnings.Add(message);

        public void Error(string category, string message, Exception? exception = null) => Errors.Add(message);
    }
}
