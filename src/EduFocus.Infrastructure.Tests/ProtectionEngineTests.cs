using EduFocus.Domain.Entities;
using EduFocus.Domain.Enums;
using EduFocus.Infrastructure.Services;
using EduFocus.Ports.Outbound.Services;

namespace EduFocus.Infrastructure.Tests;

public sealed class ProtectionEngineTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 26, 10, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task Start_stop_and_reconcile_are_idempotent_across_all_adapters()
    {
        var hosts = new FakeHosts();
        var page = new FakeBlockPage();
        var watcher = new FakeWatcher();
        var engine = Create(hosts, page, watcher);
        var rules = new[] { Rule("chatgpt.com") };

        await engine.StartAsync(rules);
        await engine.StartAsync(rules);
        Assert.True(hosts.Active);
        Assert.True(page.Active);
        Assert.True(watcher.Active);
        Assert.True(engine.IsBlockPageAvailable);

        await engine.StopAsync();
        await engine.StopAsync();
        Assert.False(hosts.Active);
        Assert.False(page.Active);
        Assert.False(watcher.Active);

        await engine.ReconcileAsync(true, rules);
        Assert.True(hosts.Active && page.Active && watcher.Active);
        await engine.ReconcileAsync(false, rules);
        Assert.False(hosts.Active || page.Active || watcher.Active);
    }

    [Fact]
    public async Task Block_page_bind_conflict_degrades_page_only_and_keeps_protection_active()
    {
        var hosts = new FakeHosts();
        var page = new FakeBlockPage { CanBind = false };
        var watcher = new FakeWatcher();
        var logger = new FakeLogger();
        var engine = new ProtectionEngine(hosts, page, watcher, logger);

        await engine.StartAsync([Rule("chatgpt.com")]);

        Assert.True(engine.IsAvailable);
        Assert.True(hosts.Active);
        Assert.True(watcher.Active);
        Assert.False(page.Active);
        Assert.False(engine.IsBlockPageAvailable);
        Assert.Single(logger.Warnings);
    }

    [Fact]
    public async Task Watcher_start_failure_rolls_back_hosts_and_listener()
    {
        var hosts = new FakeHosts();
        var page = new FakeBlockPage();
        var watcher = new FakeWatcher { ThrowOnStart = true };
        var engine = Create(hosts, page, watcher);

        await Assert.ThrowsAsync<InvalidOperationException>(() => engine.StartAsync([Rule("chatgpt.com")]));

        Assert.False(hosts.Active);
        Assert.False(page.Active);
        Assert.False(watcher.Active);
    }

    [Fact]
    public async Task Failed_stop_reconciles_resources_back_on_and_reports_failure()
    {
        var hosts = new FakeHosts();
        var page = new FakeBlockPage();
        var watcher = new FakeWatcher();
        var engine = Create(hosts, page, watcher);
        await engine.StartAsync([Rule("chatgpt.com")]);
        hosts.FailRestore = true;

        await Assert.ThrowsAsync<AggregateException>(() => engine.StopAsync());

        Assert.True(hosts.Active);
        Assert.True(page.Active);
        Assert.True(watcher.Active);
        Assert.True(engine.IsAvailable);
    }

    private static ProtectionEngine Create(FakeHosts hosts, FakeBlockPage page, FakeWatcher watcher) =>
        new(hosts, page, watcher, new FakeLogger());

    private static BlockRule Rule(string domain) =>
        BlockRule.Create(BlockRuleType.Domain, BlockMatchType.Exact, domain, BlockRuleCategory.Chatbot, BlockRuleEffect.Block, Now);

    private sealed class FakeHosts : IHostsFilePort
    {
        public bool Active { get; private set; }
        public bool FailApply { get; init; }
        public bool FailRestore { get; set; }
        public int ApplyCalls { get; private set; }
        public Task ApplyAsync(IReadOnlyCollection<BlockRule> rules, DateTimeOffset now, CancellationToken cancellationToken = default)
        {
            ApplyCalls++;
            if (FailApply) throw new UnauthorizedAccessException("simulated hosts access denial");
            Active = true;
            return Task.CompletedTask;
        }
        public Task RestoreAsync(CancellationToken cancellationToken = default)
        {
            if (FailRestore) throw new IOException("simulated hosts restore failure");
            Active = false;
            return Task.CompletedTask;
        }
    }

    private sealed class FakeBlockPage : IBlockPagePort
    {
        public bool CanBind { get; init; } = true;
        public bool Active { get; private set; }
        public bool IsAvailable => Active;
        public Task<bool> StartAsync(IReadOnlyCollection<BlockRule> rules, CancellationToken cancellationToken = default)
        {
            Active = CanBind;
            return Task.FromResult(Active);
        }
        public Task UpdateRulesAsync(IReadOnlyCollection<BlockRule> rules, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task StopAsync(CancellationToken cancellationToken = default) { Active = false; return Task.CompletedTask; }
    }

    private sealed class FakeWatcher : IProcessWatcherPort
    {
        public bool ThrowOnStart { get; init; }
        public bool Active { get; private set; }
        public bool IsRunning => Active;
        public Task StartAsync(IReadOnlyCollection<BlockRule> rules, CancellationToken cancellationToken = default)
        {
            if (ThrowOnStart) throw new InvalidOperationException("simulated watcher failure");
            Active = true;
            return Task.CompletedTask;
        }
        public Task UpdateRulesAsync(IReadOnlyCollection<BlockRule> rules, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task StopAsync(CancellationToken cancellationToken = default) { Active = false; return Task.CompletedTask; }
    }

    private sealed class FakeLogger : IAppLogger
    {
        public List<string> Warnings { get; } = [];
        public void Info(string message) { }
        public void Warning(string message) => Warnings.Add(message);
        public void Error(string message, Exception? ex = null) { }
    }
}
