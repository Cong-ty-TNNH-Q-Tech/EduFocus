using EduFocus.Domain.Attention;
using EduFocus.Domain.Entities;
using EduFocus.Domain.Enums;
using EduFocus.Domain.ValueObjects;
using EduFocus.Infrastructure.Protection;
using EduFocus.Ports.Outbound.Persistence;
using EduFocus.Ports.Outbound.Services;
using EduFocus.Ports.Protection;
using FluentAssertions;

namespace EduFocus.Infrastructure.Tests;

public sealed class ProtectionEngineTests
{
    private static readonly DateTime Now = new(2026, 10, 30, 8, 0, 0, DateTimeKind.Utc);

    private readonly FakeRuleRepository _rules = new();
    private readonly FakeHostsFileBlocker _hosts = new();
    private readonly FakeBlockPageServer _page = new();
    private readonly FakeProcessWatcher _watcher = new();
    private readonly FakeBlockedAttemptRepository _attempts = new();
    private readonly FakeStudySessionRepository _sessions = new();
    private readonly FakeClock _clock = new();
    private readonly FixedTimeProvider _time = new(Now.AddMinutes(5));
    private readonly RecordingLogger _logger = new();
    private readonly List<string> _order = [];

    public ProtectionEngineTests()
    {
        _hosts.Order = _order;
        _page.Order = _order;
        _watcher.Order = _order;
    }

    [Fact]
    public async Task Apply_starts_adapters_in_safe_order_and_filters_rules()
    {
        _rules.Rules.AddRange(
        [
            Rule(RuleType.Domain, "chatgpt.com"),
            Rule(RuleType.Domain, "claude.ai", enabled: false),
            Rule(RuleType.Domain, "expired.test", expiresAt: Now.AddDays(-1)),
            Rule(RuleType.Domain, "allowed.test"),
            Rule(RuleType.Domain, "allowed.test", BlockRuleEffect.Allow),
            Rule(RuleType.App, "discord"),
            Rule(RuleType.App, "steam"),
            Rule(RuleType.App, "steam", BlockRuleEffect.Allow),
            Rule(RuleType.Url, "https://chatgpt.com/chat")
        ]);

        await using var engine = CreateEngine();

        var warnings = await engine.ApplyAsync();

        warnings.Should().BeEmpty();
        _page.Domains.Should().Equal("chatgpt.com");
        _hosts.Applied.Should().Equal("chatgpt.com");
        _watcher.Blocked.Should().Equal("discord");
        _watcher.Allowed.Should().Equal("steam");
        _page.IsRunning.Should().BeTrue();
        _watcher.IsRunning.Should().BeTrue();

        _order.Should().ContainInOrder("page:start", "hosts:apply", "watcher:start");
    }

    [Fact]
    public async Task Apply_returns_warning_when_block_page_port_is_busy()
    {
        _rules.Rules.Add(Rule(RuleType.Domain, "chatgpt.com"));
        _page.StartResult = new BlockPageStartResult(false, 0, "Cổng 80 đang bị chiếm.");
        await using var engine = CreateEngine();

        var warnings = await engine.ApplyAsync();

        warnings.Should().ContainSingle().Which.Should().Be("Cổng 80 đang bị chiếm.");
        _hosts.Applied.Should().Equal("chatgpt.com");
        _watcher.IsRunning.Should().BeTrue();
    }

    [Fact]
    public async Task Apply_is_idempotent()
    {
        _rules.Rules.Add(Rule(RuleType.Domain, "chatgpt.com"));
        await using var engine = CreateEngine();

        await engine.ApplyAsync();
        var second = await engine.ApplyAsync();

        second.Should().BeEmpty();
        _page.StartCalls.Should().Be(1);
        _hosts.ApplyCalls.Should().Be(1);
        _watcher.StartCalls.Should().Be(1);
    }

    [Fact]
    public async Task Apply_rolls_back_when_hosts_fails()
    {
        _rules.Rules.Add(Rule(RuleType.Domain, "chatgpt.com"));
        _hosts.ApplyException = new IOException("locked");
        await using var engine = CreateEngine();

        var act = () => engine.ApplyAsync();

        await act.Should().ThrowAsync<ProtectionEngineException>();
        _page.StopCalls.Should().Be(1);
        _watcher.StopCalls.Should().Be(1);
        _hosts.RestoreCalls.Should().Be(1);
        _page.IsRunning.Should().BeFalse();
        _watcher.IsRunning.Should().BeFalse();
        _watcher.Blocked.Should().BeEmpty();
    }

    [Fact]
    public async Task Apply_rolls_back_when_watcher_fails()
    {
        _rules.Rules.Add(Rule(RuleType.App, "discord"));
        _watcher.StartException = new InvalidOperationException("wmi failed");
        await using var engine = CreateEngine();

        var act = () => engine.ApplyAsync();

        await act.Should().ThrowAsync<ProtectionEngineException>();
        _page.StopCalls.Should().Be(1);
        _hosts.RestoreCalls.Should().Be(1);
        _page.IsRunning.Should().BeFalse();
        _watcher.IsRunning.Should().BeFalse();
    }

    [Fact]
    public async Task Apply_rolls_back_when_block_page_fails()
    {
        _rules.Rules.Add(Rule(RuleType.Domain, "chatgpt.com"));
        _page.StartException = new InvalidOperationException("socket failed");
        await using var engine = CreateEngine();

        var act = () => engine.ApplyAsync();

        await act.Should().ThrowAsync<ProtectionEngineException>();
        _hosts.ApplyCalls.Should().Be(0);
        _watcher.StartCalls.Should().Be(0);
        _page.StopCalls.Should().Be(1);
    }

    [Fact]
    public async Task Failed_apply_ignores_observations()
    {
        _rules.Rules.Add(Rule(RuleType.Domain, "chatgpt.com"));
        _hosts.ApplyException = new IOException("locked");
        await using var engine = CreateEngine();

        var act = () => engine.ApplyAsync();
        await act.Should().ThrowAsync<ProtectionEngineException>();

        _page.Raise("chatgpt.com");
        await Task.Delay(100);

        _attempts.Items.Should().BeEmpty();
    }

    [Fact]
    public async Task Remove_clears_rules_stops_adapters_and_restores_hosts()
    {
        _rules.Rules.Add(Rule(RuleType.Domain, "chatgpt.com"));
        _rules.Rules.Add(Rule(RuleType.App, "discord"));
        await using var engine = CreateEngine();
        await engine.ApplyAsync();
        _order.Clear();

        var warnings = await engine.RemoveAsync();

        warnings.Should().BeEmpty();
        _watcher.Blocked.Should().BeEmpty();
        _page.Domains.Should().BeEmpty();
        _watcher.IsRunning.Should().BeFalse();
        _page.IsRunning.Should().BeFalse();
        _hosts.RestoreCalls.Should().Be(1);
        _order.Should().ContainInOrder("watcher:update", "page:update", "watcher:stop", "page:stop", "hosts:restore");
    }

    [Fact]
    public async Task Remove_returns_warning_when_watcher_stop_fails()
    {
        _rules.Rules.Add(Rule(RuleType.App, "discord"));
        _watcher.StopException = new InvalidOperationException("stuck");
        await using var engine = CreateEngine();
        await engine.ApplyAsync();

        var warnings = await engine.RemoveAsync();

        warnings.Should().ContainSingle().Which.Should().Be(ProtectionEngine.WatcherStopWarning);
        _page.IsRunning.Should().BeFalse();
        _hosts.RestoreCalls.Should().Be(1);
    }

    [Fact]
    public async Task Remove_returns_warning_when_page_stop_fails()
    {
        _rules.Rules.Add(Rule(RuleType.Domain, "chatgpt.com"));
        _page.StopException = new InvalidOperationException("stuck");
        await using var engine = CreateEngine();
        await engine.ApplyAsync();

        var warnings = await engine.RemoveAsync();

        warnings.Should().ContainSingle().Which.Should().Be(ProtectionEngine.PageStopWarning);
        _watcher.IsRunning.Should().BeFalse();
        _hosts.RestoreCalls.Should().Be(1);
    }

    [Fact]
    public async Task Remove_throws_when_hosts_restore_fails_but_stops_adapters()
    {
        _rules.Rules.Add(Rule(RuleType.Domain, "chatgpt.com"));
        _hosts.RestoreException = new IOException("locked");
        await using var engine = CreateEngine();
        await engine.ApplyAsync();

        var act = () => engine.RemoveAsync();

        await act.Should().ThrowAsync<ProtectionEngineException>();
        _watcher.IsRunning.Should().BeFalse();
        _page.IsRunning.Should().BeFalse();
    }

    [Fact]
    public async Task Remove_before_apply_is_idempotent()
    {
        await using var engine = CreateEngine();

        var first = await engine.RemoveAsync();
        var second = await engine.RemoveAsync();

        first.Should().BeEmpty();
        second.Should().BeEmpty();
        _hosts.RestoreCalls.Should().Be(2);
    }

    [Fact]
    public async Task Reconcile_active_self_heals_and_applies_once()
    {
        _rules.Rules.Add(Rule(RuleType.Domain, "chatgpt.com"));
        await using var engine = CreateEngine();

        var first = await engine.ReconcileAsync(shouldBeActive: true);
        var second = await engine.ReconcileAsync(shouldBeActive: true);

        first.Should().BeEmpty();
        second.Should().BeEmpty();
        _hosts.SelfHealCalls.Should().Be(2);
        _page.StartCalls.Should().Be(1);
        _watcher.StartCalls.Should().Be(1);
    }

    [Fact]
    public async Task Reconcile_inactive_self_heals_and_removes()
    {
        await using var engine = CreateEngine();

        var warnings = await engine.ReconcileAsync(shouldBeActive: false);

        warnings.Should().BeEmpty();
        _hosts.SelfHealCalls.Should().Be(1);
        _hosts.RestoreCalls.Should().Be(1);
        _page.StopCalls.Should().Be(1);
        _watcher.StopCalls.Should().Be(1);
    }

    [Fact]
    public async Task Reconcile_throws_when_self_heal_fails()
    {
        _hosts.SelfHealException = new IOException("denied");
        await using var engine = CreateEngine();

        var act = () => engine.ReconcileAsync(shouldBeActive: true);

        await act.Should().ThrowAsync<ProtectionEngineException>();
        _page.StartCalls.Should().Be(0);
        _hosts.ApplyCalls.Should().Be(0);
    }

    [Fact]
    public async Task Block_page_hit_writes_debounced_domain_attempt_with_session()
    {
        _rules.Rules.Add(Rule(RuleType.Domain, "chatgpt.com"));
        _sessions.Active = NewSession();
        await using var engine = CreateEngine();
        await engine.ApplyAsync();

        _page.Raise("chatgpt.com");

        var attempts = await _attempts.WaitForCountAsync(1);
        var attempt = attempts[0];
        attempt.TargetType.Should().Be(BlockedAttemptTargetType.Domain);
        attempt.ObservedVia.Should().Be(BlockedAttemptObservedVia.BlockPage);
        attempt.TargetValue.Should().Be("chatgpt.com");
        attempt.Domain!.Value.Should().Be("chatgpt.com");
        attempt.ProcessName.Should().BeNull();
        attempt.SessionId.Should().Be(_sessions.Active!.Id);

        await WaitAsync(() => _sessions.Active!.BlockedAttemptCount == 1);
        _sessions.IncrementCalls.Should().Be(1);

        _page.Raise("chatgpt.com");
        await Task.Delay(100);
        _attempts.Items.Should().HaveCount(1);

        _clock.Advance(TimeSpan.FromSeconds(31));
        _page.Raise("chatgpt.com");
        await _attempts.WaitForCountAsync(2);
    }

    [Fact]
    public async Task Process_observation_writes_app_attempt_without_window_data()
    {
        _rules.Rules.Add(Rule(RuleType.App, "discord"));
        _sessions.Active = NewSession();
        await using var engine = CreateEngine();
        await engine.ApplyAsync();

        _watcher.Raise("discord", processId: 4242);

        var attempt = (await _attempts.WaitForCountAsync(1))[0];
        attempt.TargetType.Should().Be(BlockedAttemptTargetType.App);
        attempt.ObservedVia.Should().Be(BlockedAttemptObservedVia.ProcessWatcher);
        attempt.TargetValue.Should().Be("discord");
        attempt.ProcessName.Should().Be("discord");
        attempt.WindowTitle.Should().BeNull();
        attempt.SessionId.Should().Be(_sessions.Active!.Id);
    }

    [Fact]
    public async Task Observations_are_ignored_before_apply_and_after_remove()
    {
        _sessions.Active = NewSession();
        await using var engine = CreateEngine();

        _page.Raise("chatgpt.com");
        _watcher.Raise("discord");

        await engine.ApplyAsync();
        await engine.RemoveAsync();

        _page.Raise("chatgpt.com");
        _watcher.Raise("discord");
        await Task.Delay(100);

        _attempts.Items.Should().BeEmpty();
    }

    [Fact]
    public async Task Attempt_write_failure_is_logged_and_next_attempt_is_written()
    {
        _sessions.Active = NewSession();
        _attempts.FailuresRemaining = 1;
        await using var engine = CreateEngine();
        await engine.ApplyAsync();

        _page.Raise("chatgpt.com");
        _page.Raise("claude.ai");

        await _attempts.WaitForCallsAsync(2);
        await WaitAsync(() => _attempts.Items.Count == 1);

        _logger.Messages.Should().Contain("Could not record a blocked attempt.");
    }

    [Fact]
    public async Task Process_observation_with_no_active_session_is_still_recorded()
    {
        _rules.Rules.Add(Rule(RuleType.App, "discord"));
        await using var engine = CreateEngine();
        await engine.ApplyAsync();

        _watcher.Raise("discord", processId: 7);

        var attempt = (await _attempts.WaitForCountAsync(1))[0];
        attempt.SessionId.Should().BeNull();
        _sessions.IncrementCalls.Should().Be(0);
    }

    [Fact]
    public async Task Dispose_unsubscribes_from_adapters()
    {
        _sessions.Active = NewSession();
        var engine = CreateEngine();
        await engine.ApplyAsync();
        await engine.DisposeAsync();

        _page.Raise("chatgpt.com");
        _watcher.Raise("discord");
        await Task.Delay(100);

        _attempts.Items.Should().BeEmpty();
    }

    [Fact]
    public void Invalid_debounce_is_rejected()
    {
        var act = () => new ProtectionEngine(
            _rules,
            _hosts,
            _page,
            _watcher,
            _attempts,
            _sessions,
            _clock,
            _logger,
            _time,
            new ProtectionEngineOptions { Debounce = TimeSpan.FromSeconds(-1) });

        act.Should().Throw<ArgumentOutOfRangeException>();
    }

    private ProtectionEngine CreateEngine() => new(
        _rules,
        _hosts,
        _page,
        _watcher,
        _attempts,
        _sessions,
        _clock,
        _logger,
        _time,
        new ProtectionEngineOptions { Debounce = TimeSpan.FromSeconds(30) });

    private static StudySession NewSession() =>
        StudySession.Start(Now, new DateOnly(2026, 10, 30), SessionTrigger.Manual, monitoringEnabled: true);

    private static BlockRule Rule(
        RuleType ruleType,
        string value,
        BlockRuleEffect effect = BlockRuleEffect.Block,
        bool enabled = true,
        DateTime? expiresAt = null) =>
        BlockRule.Create(
            ruleType,
            RuleMatchType.Exact,
            value,
            ruleType == RuleType.App ? BlockRuleCategory.Game : BlockRuleCategory.Chatbot,
            effect,
            Now.AddDays(-2),
            isEnabled: enabled,
            isBuiltIn: false,
            expiresAtUtc: expiresAt);

    private static async Task WaitAsync(Func<bool> condition)
    {
        var deadline = DateTime.UtcNow.AddSeconds(5);
        while (DateTime.UtcNow < deadline)
        {
            if (condition())
                return;

            await Task.Delay(10);
        }

        throw new TimeoutException("Condition was not met within 5 seconds.");
    }

    private sealed class FakeRuleRepository : IBlockRuleRepository
    {
        public List<BlockRule> Rules { get; } = [];

        public Task<IReadOnlyList<BlockRule>> ListAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<BlockRule>>(Rules.ToArray());

        public Task<BlockRule?> GetAsync(BlockRuleId id, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task AddAsync(BlockRule rule, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task UpdateAsync(BlockRule rule, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task DeleteAsync(BlockRuleId id, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
    }

    private sealed class FakeHostsFileBlocker : IHostsFileBlocker
    {
        public List<string> Order { get; set; } = [];

        public List<string> Applied { get; } = [];

        public int ApplyCalls { get; private set; }

        public int RestoreCalls { get; private set; }

        public int SelfHealCalls { get; private set; }

        public Exception? ApplyException { get; set; }

        public Exception? RestoreException { get; set; }

        public Exception? SelfHealException { get; set; }

        public Task<HostsApplyResult> ApplyAsync(
            IReadOnlyCollection<string> domains,
            CancellationToken cancellationToken = default)
        {
            ApplyCalls++;
            Order.Add("hosts:apply");

            if (ApplyException is not null)
                throw ApplyException;

            Applied.Clear();
            Applied.AddRange(domains);
            return Task.FromResult(new HostsApplyResult(true, domains.Count, domains.ToArray(), "backup"));
        }

        public Task<HostsRestoreResult> RestoreAsync(CancellationToken cancellationToken = default)
        {
            RestoreCalls++;
            Order.Add("hosts:restore");

            if (RestoreException is not null)
                throw RestoreException;

            Applied.Clear();
            return Task.FromResult(new HostsRestoreResult(true, true));
        }

        public Task<HostsRecoveryResult> SelfHealAsync(CancellationToken cancellationToken = default)
        {
            SelfHealCalls++;

            if (SelfHealException is not null)
                throw SelfHealException;

            return Task.FromResult(new HostsRecoveryResult(false, false));
        }
    }

    private sealed class FakeBlockPageServer : IBlockPageServer
    {
        public List<string> Order { get; set; } = [];

        public List<string> Domains { get; } = [];

        public int StartCalls { get; private set; }

        public int StopCalls { get; private set; }

        public BlockPageStartResult StartResult { get; set; } = new(true, 80, null);

        public Exception? StartException { get; set; }

        public Exception? StopException { get; set; }

        public bool IsRunning { get; private set; }

        public int Port => 80;

        public event Action<BlockPageHit>? BlockedRequestObserved;

        public Task<BlockPageStartResult> StartAsync(
            IReadOnlyCollection<string> blockedDomains,
            CancellationToken cancellationToken = default)
        {
            StartCalls++;
            Order.Add("page:start");

            if (StartException is not null)
                throw StartException;

            Domains.Clear();
            Domains.AddRange(blockedDomains);
            IsRunning = StartResult.Started;
            return Task.FromResult(StartResult);
        }

        public void UpdateDomains(IReadOnlyCollection<string> blockedDomains)
        {
            Order.Add("page:update");
            Domains.Clear();
            Domains.AddRange(blockedDomains);
        }

        public Task StopAsync(CancellationToken cancellationToken = default)
        {
            StopCalls++;
            Order.Add("page:stop");

            if (StopException is not null)
                throw StopException;

            IsRunning = false;
            return Task.CompletedTask;
        }

        public void Raise(string domain) =>
            BlockedRequestObserved?.Invoke(new BlockPageHit(DateTimeOffset.UtcNow, domain));
    }

    private sealed class FakeProcessWatcher : IProcessWatcher
    {
        public List<string> Order { get; set; } = [];

        public List<string> Blocked { get; } = [];

        public List<string> Allowed { get; } = [];

        public int StartCalls { get; private set; }

        public int StopCalls { get; private set; }

        public Exception? StartException { get; set; }

        public Exception? StopException { get; set; }

        public bool IsRunning { get; private set; }

        public event Action<ProcessBlockedObservation>? ProcessBlockedObserved;

        public Task StartAsync(
            IReadOnlyCollection<string> blockedProcessNames,
            IReadOnlyCollection<string> allowedProcessNames,
            CancellationToken cancellationToken = default)
        {
            StartCalls++;
            Order.Add("watcher:start");

            if (StartException is not null)
                throw StartException;

            Blocked.Clear();
            Blocked.AddRange(blockedProcessNames);
            Allowed.Clear();
            Allowed.AddRange(allowedProcessNames);
            IsRunning = true;
            return Task.CompletedTask;
        }

        public void UpdateRules(
            IReadOnlyCollection<string> blockedProcessNames,
            IReadOnlyCollection<string> allowedProcessNames)
        {
            Order.Add("watcher:update");
            Blocked.Clear();
            Blocked.AddRange(blockedProcessNames);
            Allowed.Clear();
            Allowed.AddRange(allowedProcessNames);
        }

        public Task StopAsync(CancellationToken cancellationToken = default)
        {
            StopCalls++;
            Order.Add("watcher:stop");

            if (StopException is not null)
                throw StopException;

            IsRunning = false;
            return Task.CompletedTask;
        }

        public void Raise(string processName, int processId = 123) =>
            ProcessBlockedObserved?.Invoke(new ProcessBlockedObservation(
                DateTimeOffset.UtcNow,
                processName,
                processId,
                ProcessBlockOutcome.ClosedGracefully,
                null));
    }

    private sealed class FakeBlockedAttemptRepository : IBlockedAttemptRepository
    {
        private readonly object _lock = new();
        private readonly List<BlockedAttempt> _items = [];
        private int _calls;
        private int _failuresRemaining;

        public IReadOnlyList<BlockedAttempt> Items
        {
            get
            {
                lock (_lock)
                    return _items.ToArray();
            }
        }

        public int FailuresRemaining
        {
            get => Volatile.Read(ref _failuresRemaining);
            set => Volatile.Write(ref _failuresRemaining, value);
        }

        public Task AddAsync(BlockedAttempt attempt, CancellationToken cancellationToken = default)
        {
            Interlocked.Increment(ref _calls);

            if (Interlocked.Decrement(ref _failuresRemaining) >= 0)
                throw new IOException("database busy");

            lock (_lock)
                _items.Add(attempt);

            return Task.CompletedTask;
        }

        public async Task<IReadOnlyList<BlockedAttempt>> WaitForCountAsync(int count)
        {
            await WaitForCallsAsync(count);
            return Items;
        }

        public async Task WaitForCallsAsync(int count)
        {
            var deadline = DateTime.UtcNow.AddSeconds(5);
            while (DateTime.UtcNow < deadline)
            {
                if (Volatile.Read(ref _calls) >= count)
                    return;

                await Task.Delay(10);
            }

            throw new TimeoutException($"Blocked attempt repository was not called {count} times.");
        }
    }

    private sealed class FakeStudySessionRepository : IStudySessionRepository
    {
        private readonly object _lock = new();

        public StudySession? Active { get; set; }

        public List<StudySession> Sessions { get; } = [];

        public int UpdateCalls { get; private set; }

        public int IncrementCalls { get; private set; }

        public Task<StudySession?> GetActiveAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(Active);

        public Task AddAsync(StudySession session, CancellationToken cancellationToken = default)
        {
            lock (_lock)
                Sessions.Add(session);

            return Task.CompletedTask;
        }

        public Task UpdateAsync(StudySession session, CancellationToken cancellationToken = default)
        {
            lock (_lock)
                UpdateCalls++;

            return Task.CompletedTask;
        }

        public Task<bool> RecordHeartbeatAsync(
            StudySessionId sessionId,
            DateTime heartbeatAtUtc,
            int activeSeconds,
            CancellationToken cancellationToken = default)
        {
            lock (_lock)
            {
                var session = Active;

                if (session is null || session.Id != sessionId || !session.IsActive)
                    return Task.FromResult(false);

                var beat = heartbeatAtUtc < session.UpdatedAtUtc
                    ? session.UpdatedAtUtc
                    : heartbeatAtUtc;

                session.Heartbeat(beat);
                session.AddActiveSeconds(activeSeconds, beat);
                return Task.FromResult(true);
            }
        }

        public Task<bool> IncrementBlockedAttemptCountAsync(
            StudySessionId sessionId,
            DateTime nowUtc,
            int count = 1,
            CancellationToken cancellationToken = default)
        {
            lock (_lock)
            {
                IncrementCalls++;

                var session = Active;

                if (session is null || session.Id != sessionId || !session.IsActive)
                    return Task.FromResult(false);

                var stamp = nowUtc < session.UpdatedAtUtc ? session.UpdatedAtUtc : nowUtc;
                session.IncrementBlockedAttempts(stamp, count);
                return Task.FromResult(true);
            }
        }

        public Task<bool> AddActivitySecondsAsync(
            StudySessionId sessionId,
            ActivityCategory category,
            int categorySeconds,
            int idleSeconds,
            int onlineClassSeconds,
            DateTime nowUtc,
            CancellationToken cancellationToken = default)
        {
            lock (_lock)
            {
                var session = Active;

                if (session is null || session.Id != sessionId || !session.IsActive)
                    return Task.FromResult(false);

                var stamp = nowUtc < session.UpdatedAtUtc ? session.UpdatedAtUtc : nowUtc;
                session.AddCategorySeconds(category, categorySeconds, stamp);
                if (idleSeconds > 0)
                    session.AddIdleSeconds(idleSeconds, stamp);
                if (onlineClassSeconds > 0)
                    session.AddOnlineClassSeconds(onlineClassSeconds, stamp);

                return Task.FromResult(true);
            }
        }

        public Task<bool> AddAttentionSecondsAsync(
            StudySessionId sessionId,
            AttentionState state,
            int seconds,
            double? confidence,
            DateTime nowUtc,
            CancellationToken cancellationToken = default)
        {
            lock (_lock)
            {
                var session = Active;

                if (session is null || session.Id != sessionId || !session.IsActive)
                    return Task.FromResult(false);

                var stamp = nowUtc < session.UpdatedAtUtc ? session.UpdatedAtUtc : nowUtc;
                session.AddAttentionSeconds(state, seconds, stamp);

                if (confidence is not null && state != AttentionState.CameraUnavailable)
                    session.SetAttentionConfidence(confidence.Value, stamp);

                return Task.FromResult(true);
            }
        }
    }

    private sealed class FakeClock : IMonotonicClockPort
    {
        private long _ticks;

        public TimeSpan GetTickCount() => TimeSpan.FromTicks(Volatile.Read(ref _ticks));

        public void Advance(TimeSpan delta) => Interlocked.Add(ref _ticks, delta.Ticks);
    }

    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        private DateTimeOffset _now = now;

        public override DateTimeOffset GetUtcNow() => _now;

        public override TimeZoneInfo LocalTimeZone => TimeZoneInfo.Utc;
    }

    private sealed class RecordingLogger : IAppLogger
    {
        private readonly object _lock = new();
        private readonly List<string> _messages = [];

        public IReadOnlyList<string> Messages
        {
            get
            {
                lock (_lock)
                    return _messages.ToArray();
            }
        }

        public void Info(string category, string message) => Add(message);

        public void Warning(string category, string message) => Add(message);

        public void Error(string category, string message, Exception? exception = null) => Add(message);

        private void Add(string message)
        {
            lock (_lock)
                _messages.Add(message);
        }
    }
}
