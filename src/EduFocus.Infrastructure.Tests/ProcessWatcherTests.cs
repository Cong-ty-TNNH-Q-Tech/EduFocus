using EduFocus.Domain.Entities;
using EduFocus.Domain.Enums;
using EduFocus.Infrastructure.Services;
using EduFocus.Ports.Outbound.Services;
using Microsoft.Extensions.DependencyInjection;

namespace EduFocus.Infrastructure.Tests;

public sealed class ProcessWatcherTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 26, 10, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task Matching_app_sends_close_and_records_observed_attempt_once_per_debounce_window()
    {
        var process = new FakeProcess(2001, "blocked", "C:\\Apps\\blocked.exe") { CloseSucceeds = true };
        var provider = new FakeProvider(() => [process.Clone()]);
        var recorder = new FakeRecorder();
        await using var watcher = CreateWatcher(provider, recorder, new FakeLogger());
        await watcher.UpdateRulesAsync([AppRule("blocked.exe")]);

        await watcher.ScanOnceAsync(Now);
        await watcher.ScanOnceAsync(Now.AddSeconds(10));

        Assert.Equal(2, provider.ScanCount);
        Assert.Single(recorder.Attempts);
        Assert.Equal("blocked", recorder.Attempts[0]);
        Assert.Equal(1, provider.LastProcesses.Sum(x => x.CloseCalls));
        Assert.DoesNotContain(provider.LastProcesses, x => x.Killed);
    }

    [Fact]
    public async Task Hung_blocked_app_is_killed_as_a_process_tree_after_close_grace()
    {
        var instance = new FakeProcess(2002, "blocked", "C:\\Apps\\blocked.exe") { CloseSucceeds = false };
        var provider = new FakeProvider(() => [instance]);
        var recorder = new FakeRecorder();
        await using var watcher = CreateWatcher(provider, recorder, new FakeLogger(), closeGrace: TimeSpan.FromSeconds(2));
        await watcher.UpdateRulesAsync([AppRule("C:\\Apps\\blocked.exe")]);

        await watcher.ScanOnceAsync(Now);

        Assert.Equal(1, instance.CloseCalls);
        Assert.Equal(TimeSpan.FromSeconds(2), instance.LastWait);
        Assert.True(instance.Killed);
        Assert.Single(recorder.Attempts);
    }

    [Fact]
    public async Task Allow_rules_system_processes_and_processes_without_windows_are_never_killed()
    {
        var allowed = new FakeProcess(2003, "allowed", null);
        var system = new FakeProcess(2004, "svchost", null);
        var headless = new FakeProcess(2005, "blocked", null) { HasWindow = false };
        var provider = new FakeProvider(() => [allowed, system, headless]);
        var recorder = new FakeRecorder();
        await using var watcher = CreateWatcher(provider, recorder, new FakeLogger());
        await watcher.UpdateRulesAsync([
            AppRule("allowed.exe", BlockRuleEffect.Block),
            AppRule("allowed.exe", BlockRuleEffect.Allow),
            AppRule("blocked.exe")
        ]);

        await watcher.ScanOnceAsync(Now);

        Assert.False(allowed.Killed);
        Assert.False(system.Killed);
        Assert.False(headless.Killed);
        Assert.Empty(recorder.Attempts);
    }

    [Fact]
    public async Task Access_denied_does_not_crash_watcher_or_repeat_notifications_and_lifecycle_stops()
    {
        var denied = new FakeProcess(2006, "blocked", "C:\\Apps\\blocked.exe") { ThrowOnKill = true };
        var provider = new FakeProvider(() => [denied.Clone(), denied.Clone()]);
        var recorder = new FakeRecorder();
        var logger = new FakeLogger();
        await using var watcher = CreateWatcher(provider, recorder, logger);
        await watcher.UpdateRulesAsync([AppRule("blocked.exe")]);

        await watcher.ScanOnceAsync(Now);
        await watcher.ScanOnceAsync(Now.AddSeconds(10));

        Assert.Single(logger.Warnings);
        Assert.Single(recorder.Attempts);
        Assert.False(watcher.IsRunning);
        await watcher.StartAsync([]);
        Assert.True(watcher.IsRunning);
        await watcher.StopAsync();
        Assert.False(watcher.IsRunning);
    }

    private static ProcessWatcher CreateWatcher(FakeProvider provider, FakeRecorder recorder, FakeLogger logger, TimeSpan? closeGrace = null)
    {
        var services = new ServiceCollection();
        services.AddSingleton<IBlockedAttemptRecorder>(recorder);
        var root = services.BuildServiceProvider();
        return new ProcessWatcher(provider, root.GetRequiredService<IServiceScopeFactory>(), logger,
            pollInterval: TimeSpan.FromHours(1), closeGrace: closeGrace);
    }

    private static BlockRule AppRule(string value, BlockRuleEffect effect = BlockRuleEffect.Block) =>
        BlockRule.Create(BlockRuleType.App, BlockMatchType.Exact, value, BlockRuleCategory.Custom, effect, Now);

    private sealed class FakeProvider(Func<IReadOnlyList<IWatchedProcess>> create) : IProcessSnapshotProvider
    {
        public int ScanCount { get; private set; }
        public IReadOnlyList<FakeProcess> LastProcesses { get; private set; } = [];
        public IReadOnlyList<IWatchedProcess> GetProcesses()
        {
            ScanCount++;
            LastProcesses = create().Cast<FakeProcess>().ToArray();
            return LastProcesses;
        }
    }

    private sealed class FakeProcess(int id, string name, string? path) : IWatchedProcess
    {
        public int Id { get; } = id;
        public string Name { get; } = name;
        public string? ExecutablePath { get; } = path;
        public bool HasWindow { get; set; } = true;
        public bool IsExited { get; private set; }
        public bool CloseSucceeds { get; set; }
        public bool ThrowOnKill { get; set; }
        public bool Killed { get; private set; }
        public int CloseCalls { get; private set; }
        public TimeSpan LastWait { get; private set; }
        public bool TryCloseMainWindow()
        {
            CloseCalls++;
            if (CloseSucceeds) IsExited = true;
            return CloseSucceeds;
        }
        public Task<bool> WaitForExitAsync(TimeSpan timeout, CancellationToken cancellationToken)
        {
            LastWait = timeout;
            return Task.FromResult(IsExited);
        }
        public void KillTree()
        {
            if (ThrowOnKill) throw new UnauthorizedAccessException("simulated process access denied");
            Killed = true;
            IsExited = true;
        }
        public IWatchedProcess Clone() => new FakeProcess(Id, Name, ExecutablePath)
        { HasWindow = HasWindow, CloseSucceeds = CloseSucceeds, ThrowOnKill = ThrowOnKill };
        public void Dispose() { }
    }

    private sealed class FakeRecorder : IBlockedAttemptRecorder
    {
        public List<string> Attempts { get; } = [];
        public Task RecordBlockedAppAsync(string processName, DateTimeOffset occurredAtUtc, CancellationToken cancellationToken = default)
        { Attempts.Add(processName); return Task.CompletedTask; }
    }

    private sealed class FakeLogger : IAppLogger
    {
        public List<string> Warnings { get; } = [];
        public void Info(string message) { }
        public void Warning(string message) => Warnings.Add(message);
        public void Error(string message, Exception? ex = null) { }
    }
}
