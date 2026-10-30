using System.Collections.Concurrent;
using System.Threading.Channels;
using EduFocus.Infrastructure.Protection;
using EduFocus.Ports.Outbound.Services;
using FluentAssertions;

namespace EduFocus.Infrastructure.Tests;

public sealed class ProcessWatcherTests
{
    private static readonly TimeSpan EventTimeout = TimeSpan.FromSeconds(5);

    private readonly FakeProcessController _controller = new();
    private readonly FakeClock _clock = new();
    private readonly RecordingLogger _logger = new();

    [Fact]
    public async Task Closes_blocked_windowed_process_gracefully()
    {
        _controller.SetProcesses(new ProcessSnapshot(100, "chrome", HasMainWindow: true));
        var reader = Track(out var watcher);

        await using (watcher)
        {
            await watcher.StartAsync(["chrome"], []);

            var observation = await NextAsync(reader);

            observation.ProcessName.Should().Be("chrome");
            observation.ProcessId.Should().Be(100);
            observation.Outcome.Should().Be(ProcessBlockOutcome.ClosedGracefully);
            observation.Warning.Should().BeNull();
            _controller.CloseCalls.Should().Equal(100);
            _controller.KillCalls.Should().BeEmpty();
        }
    }

    [Fact]
    public async Task Kills_process_and_warns_when_close_times_out()
    {
        _controller.SetProcesses(new ProcessSnapshot(101, "discord", HasMainWindow: true));
        _controller.CloseResult = _ => false;
        var reader = Track(out var watcher);

        await using (watcher)
        {
            await watcher.StartAsync(["discord"], []);

            var observation = await NextAsync(reader);

            observation.Outcome.Should().Be(ProcessBlockOutcome.Killed);
            observation.Warning.Should().Contain("dữ liệu chưa lưu");
            _controller.KillCalls.Should().Equal(101);
            _logger.Messages.Should().Contain(message => message.Contains("unsaved data"));
        }
    }

    [Fact]
    public async Task Kills_process_when_close_request_throws()
    {
        _controller.SetProcesses(new ProcessSnapshot(102, "steam", HasMainWindow: true));
        _controller.CloseException = new InvalidOperationException("access denied");
        var reader = Track(out var watcher);

        await using (watcher)
        {
            await watcher.StartAsync(["steam"], []);

            var observation = await NextAsync(reader);

            observation.Outcome.Should().Be(ProcessBlockOutcome.Killed);
            _controller.KillCalls.Should().Equal(102);
            _logger.Messages.Should().Contain(message => message.Contains("Close request failed"));
        }
    }

    [Fact]
    public async Task Reports_failed_outcome_when_kill_throws()
    {
        _controller.SetProcesses(new ProcessSnapshot(103, "game", HasMainWindow: true));
        _controller.CloseResult = _ => false;
        _controller.KillException = new InvalidOperationException("denied");
        var reader = Track(out var watcher);

        await using (watcher)
        {
            await watcher.StartAsync(["game"], []);

            var observation = await NextAsync(reader);

            observation.Outcome.Should().Be(ProcessBlockOutcome.Failed);
            observation.Warning.Should().Contain("quyền");
            _controller.KillCalls.Should().Equal(103);
        }
    }

    [Fact]
    public async Task Allowlisted_process_is_never_touched()
    {
        _controller.SetProcesses(
            new ProcessSnapshot(200, "chrome", HasMainWindow: true),
            new ProcessSnapshot(201, "notepad", HasMainWindow: true));
        var reader = Track(out var watcher);

        await using (watcher)
        {
            await watcher.StartAsync(["chrome", "notepad"], [@"C:\Program Files\Google\Chrome\chrome.exe"]);

            var observation = await NextAsync(reader);

            observation.ProcessId.Should().Be(201);
            _controller.CloseCalls.Should().Equal(201);
            _controller.KillCalls.Should().BeEmpty();
        }
    }

    [Fact]
    public async Task Windowless_process_is_skipped()
    {
        _controller.SetProcesses(
            new ProcessSnapshot(300, "chrome", HasMainWindow: false),
            new ProcessSnapshot(301, "notepad", HasMainWindow: true));
        var reader = Track(out var watcher);

        await using (watcher)
        {
            await watcher.StartAsync(["chrome", "notepad"], []);

            var observation = await NextAsync(reader);

            observation.ProcessId.Should().Be(301);
            _controller.CloseCalls.Should().Equal(301);
        }
    }

    [Fact]
    public async Task Protected_system_and_edufocus_processes_are_skipped()
    {
        _controller.SetProcesses(
            new ProcessSnapshot(400, "explorer", HasMainWindow: true),
            new ProcessSnapshot(401, "edufocus", HasMainWindow: true),
            new ProcessSnapshot(402, "notepad", HasMainWindow: true));
        var reader = Track(out var watcher);

        await using (watcher)
        {
            await watcher.StartAsync(["explorer", "edufocus", "notepad"], []);

            var observation = await NextAsync(reader);

            observation.ProcessId.Should().Be(402);
            _controller.CloseCalls.Should().Equal(402);
        }
    }

    [Fact]
    public async Task Own_process_and_pid_four_or_lower_are_skipped()
    {
        _controller.SetProcesses(
            new ProcessSnapshot(3, "chrome", HasMainWindow: true),
            new ProcessSnapshot(Environment.ProcessId, "chrome", HasMainWindow: true),
            new ProcessSnapshot(500, "notepad", HasMainWindow: true));
        var reader = Track(out var watcher);

        await using (watcher)
        {
            await watcher.StartAsync(["chrome", "notepad"], []);

            var observation = await NextAsync(reader);

            observation.ProcessId.Should().Be(500);
            _controller.CloseCalls.Should().Equal(500);
        }
    }

    [Fact]
    public async Task Matches_normalized_process_names()
    {
        _controller.SetProcesses(new ProcessSnapshot(600, "Chrome.EXE", HasMainWindow: true));
        var reader = Track(out var watcher);

        await using (watcher)
        {
            await watcher.StartAsync([@"C:\Program Files\Google\Chrome\chrome.exe"], []);

            var observation = await NextAsync(reader);

            observation.ProcessName.Should().Be("chrome");
            observation.ProcessId.Should().Be(600);
        }
    }

    [Fact]
    public async Task Debounces_repeated_attempts_until_clock_advances()
    {
        _controller.SetProcesses(new ProcessSnapshot(700, "chrome", HasMainWindow: true));
        var reader = Track(out var watcher);

        await using (watcher)
        {
            await watcher.StartAsync(["chrome"], []);

            var first = await NextAsync(reader);
            first.Outcome.Should().Be(ProcessBlockOutcome.ClosedGracefully);

            await Task.Delay(TimeSpan.FromMilliseconds(150));
            reader.TryRead(out _).Should().BeFalse();
            _controller.CloseCalls.Should().Equal(700);

            _clock.Advance(TimeSpan.FromSeconds(31));

            var second = await NextAsync(reader);
            second.ProcessId.Should().Be(700);
            _controller.CloseCalls.Should().Equal(700, 700);
        }
    }

    [Fact]
    public async Task Relaunched_process_with_new_pid_is_closed_immediately_within_debounce_window()
    {
        _controller.SetProcesses(new ProcessSnapshot(701, "discord", HasMainWindow: true));
        var reader = Track(out var watcher);

        await using (watcher)
        {
            await watcher.StartAsync(["discord"], []);

            var first = await NextAsync(reader);
            first.ProcessId.Should().Be(701);
            first.Outcome.Should().Be(ProcessBlockOutcome.ClosedGracefully);

            // Student immediately relaunches the blocked app -> new PID 702 without clock advancing
            _controller.SetProcesses(new ProcessSnapshot(702, "discord", HasMainWindow: true));

            var second = await NextAsync(reader);
            second.ProcessId.Should().Be(702);
            second.Outcome.Should().Be(ProcessBlockOutcome.ClosedGracefully);
            _controller.CloseCalls.Should().Equal(701, 702);
        }
    }

    [Fact]
    public async Task Update_rules_apply_without_restarting()
    {
        _controller.SetProcesses(new ProcessSnapshot(800, "notepad", HasMainWindow: true));
        var reader = Track(out var watcher);

        await using (watcher)
        {
            await watcher.StartAsync([], []);

            watcher.UpdateRules(["notepad"], []);

            var observation = await NextAsync(reader);

            observation.ProcessId.Should().Be(800);
            watcher.IsRunning.Should().BeTrue();
        }
    }

    [Fact]
    public async Task Stop_halts_scanning_and_restart_works()
    {
        var reader = Track(out var watcher);

        await using (watcher)
        {
            await watcher.StartAsync(["notepad"], []);
            await watcher.StopAsync();

            watcher.IsRunning.Should().BeFalse();

            _controller.SetProcesses(new ProcessSnapshot(900, "notepad", HasMainWindow: true));
            await Task.Delay(TimeSpan.FromMilliseconds(150));
            reader.TryRead(out _).Should().BeFalse();
            _controller.CloseCalls.Should().BeEmpty();

            await watcher.StartAsync(["notepad"], []);

            var observation = await NextAsync(reader);
            observation.ProcessId.Should().Be(900);
        }
    }

    [Fact]
    public async Task Stop_before_start_is_a_noop()
    {
        await using var watcher = CreateWatcher();

        await watcher.StopAsync();

        watcher.IsRunning.Should().BeFalse();
        _logger.Messages.Should().BeEmpty();
    }

    [Fact]
    public async Task Handler_failure_does_not_stop_the_watcher()
    {
        _controller.SetProcesses(new ProcessSnapshot(1000, "chrome", HasMainWindow: true));
        await using var watcher = CreateWatcher();
        var first = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var second = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var calls = 0;

        watcher.ProcessBlockedObserved += _ =>
        {
            if (Interlocked.Increment(ref calls) == 1)
            {
                first.TrySetResult();
                throw new InvalidOperationException("handler boom");
            }

            second.TrySetResult();
        };

        await watcher.StartAsync(["chrome"], []);

        await first.Task.WaitAsync(EventTimeout);
        _clock.Advance(TimeSpan.FromSeconds(31));
        await second.Task.WaitAsync(EventTimeout);

        calls.Should().Be(2);
        watcher.IsRunning.Should().BeTrue();
        _logger.Messages.Should().Contain(message => message.Contains("handler failed"));
    }

    [Fact]
    public async Task Scan_failure_is_logged_once_and_recovers()
    {
        _controller.GetFailuresRemaining = 1;
        _controller.SetProcesses(new ProcessSnapshot(1100, "notepad", HasMainWindow: true));
        var reader = Track(out var watcher);

        await using (watcher)
        {
            await watcher.StartAsync(["notepad"], []);

            var observation = await NextAsync(reader);

            observation.ProcessId.Should().Be(1100);
            _logger.Messages.Should().ContainSingle(message => message.Contains("scan failed"));
        }
    }

    [Fact]
    public async Task Invalid_process_names_are_rejected()
    {
        await using var watcher = CreateWatcher();

        var blank = () => watcher.StartAsync(["   "], []);
        var tooLong = () => watcher.StartAsync([new string('a', 129)], []);
        var updateBlank = () => watcher.UpdateRules([""], []);

        await blank.Should().ThrowAsync<ArgumentException>();
        await tooLong.Should().ThrowAsync<ArgumentException>();
        updateBlank.Should().Throw<ArgumentException>();
        watcher.IsRunning.Should().BeFalse();
    }

    [Fact]
    public void Invalid_options_are_rejected()
    {
        var poll = () => new ProcessWatcher(
            new ProcessWatcherOptions { PollInterval = TimeSpan.FromMilliseconds(1) },
            _controller,
            _clock,
            _logger);
        var close = () => new ProcessWatcher(
            new ProcessWatcherOptions { CloseTimeout = TimeSpan.Zero },
            _controller,
            _clock,
            _logger);
        var debounce = () => new ProcessWatcher(
            new ProcessWatcherOptions { Debounce = TimeSpan.FromSeconds(-1) },
            _controller,
            _clock,
            _logger);

        poll.Should().Throw<ArgumentOutOfRangeException>();
        close.Should().Throw<ArgumentOutOfRangeException>();
        debounce.Should().Throw<ArgumentOutOfRangeException>();
    }

    [Fact]
    public async Task Start_and_stop_are_logged_without_process_details()
    {
        await using var watcher = CreateWatcher();

        await watcher.StartAsync([], []);
        await watcher.StopAsync();

        _logger.Messages.Should().Contain("Process watcher started.");
        _logger.Messages.Should().Contain("Process watcher stopped.");
        _logger.Messages.Should().NotContain(message => message.Contains("chrome"));
    }

    private ProcessWatcher CreateWatcher() => new(
        new ProcessWatcherOptions
        {
            PollInterval = TimeSpan.FromMilliseconds(20),
            CloseTimeout = TimeSpan.FromMilliseconds(50),
            Debounce = TimeSpan.FromSeconds(30)
        },
        _controller,
        _clock,
        _logger);

    private ChannelReader<ProcessBlockedObservation> Track(out ProcessWatcher watcher)
    {
        watcher = CreateWatcher();
        var channel = Channel.CreateUnbounded<ProcessBlockedObservation>();
        watcher.ProcessBlockedObserved += observation => channel.Writer.TryWrite(observation);
        return channel.Reader;
    }

    private static async Task<ProcessBlockedObservation> NextAsync(
        ChannelReader<ProcessBlockedObservation> reader)
    {
        using var cts = new CancellationTokenSource(EventTimeout);
        try
        {
            return await reader.ReadAsync(cts.Token);
        }
        catch (OperationCanceledException)
        {
            throw new TimeoutException("No process observation arrived within 5 seconds.");
        }
    }

    private sealed class FakeProcessController : IProcessController
    {
        private readonly object _lock = new();
        private readonly List<ProcessSnapshot> _processes = [];
        private readonly List<int> _closeCalls = [];
        private readonly List<int> _killCalls = [];
        private int _getFailuresRemaining;

        public Func<int, bool> CloseResult { get; set; } = _ => true;

        public Exception? CloseException { get; set; }

        public Exception? KillException { get; set; }

        public int GetFailuresRemaining
        {
            get => Volatile.Read(ref _getFailuresRemaining);
            set => Volatile.Write(ref _getFailuresRemaining, value);
        }

        public IReadOnlyList<int> CloseCalls
        {
            get
            {
                lock (_lock)
                    return _closeCalls.ToArray();
            }
        }

        public IReadOnlyList<int> KillCalls
        {
            get
            {
                lock (_lock)
                    return _killCalls.ToArray();
            }
        }

        public void SetProcesses(params ProcessSnapshot[] processes)
        {
            lock (_lock)
            {
                _processes.Clear();
                _processes.AddRange(processes);
            }
        }

        public IReadOnlyList<ProcessSnapshot> GetProcesses()
        {
            if (Interlocked.Decrement(ref _getFailuresRemaining) >= 0)
                throw new InvalidOperationException("enumeration failed");

            lock (_lock)
                return _processes.ToArray();
        }

        public Task<bool> CloseMainWindowAsync(
            int processId,
            TimeSpan timeout,
            CancellationToken cancellationToken = default)
        {
            lock (_lock)
                _closeCalls.Add(processId);

            if (CloseException is not null)
                throw CloseException;

            return Task.FromResult(CloseResult(processId));
        }

        public void KillProcessTree(int processId)
        {
            lock (_lock)
                _killCalls.Add(processId);

            if (KillException is not null)
                throw KillException;
        }
    }

    private sealed class FakeClock : IMonotonicClockPort
    {
        private long _ticks;

        public TimeSpan GetTickCount() => TimeSpan.FromTicks(Volatile.Read(ref _ticks));

        public void Advance(TimeSpan delta) => Interlocked.Add(ref _ticks, delta.Ticks);
    }

    private sealed class RecordingLogger : IAppLogger
    {
        private readonly ConcurrentQueue<string> _messages = new();

        public IReadOnlyList<string> Messages => _messages.ToArray();

        public void Info(string category, string message) => _messages.Enqueue(message);

        public void Warning(string category, string message) => _messages.Enqueue(message);

        public void Error(string category, string message, Exception? exception = null) =>
            _messages.Enqueue(message);
    }
}
