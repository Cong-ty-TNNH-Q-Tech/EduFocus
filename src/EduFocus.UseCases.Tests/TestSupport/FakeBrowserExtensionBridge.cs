using EduFocus.Domain.Entities;
using EduFocus.Ports.Outbound.Services;

namespace EduFocus.UseCases.Tests.TestSupport;

internal sealed class FakeBrowserExtensionBridge : IBrowserExtensionBridge
{
    public bool IsRunning { get; set; } = true;

    public int StartCalls { get; private set; }

    public int UpdateCalls { get; private set; }

    public int StopCalls { get; private set; }

    public IReadOnlyList<BlockRule> LastRules { get; private set; } = [];

    public DateTime? LastNowUtc { get; private set; }

    public event EventHandler<BrowserBlockedAttempt>? BlockedAttemptObserved;

    public Task StartAsync(
        IReadOnlyList<BlockRule> rules,
        DateTime nowUtc,
        CancellationToken cancellationToken = default)
    {
        StartCalls++;
        LastRules = rules;
        LastNowUtc = nowUtc;
        return Task.CompletedTask;
    }

    public Task UpdateRulesAsync(
        IReadOnlyList<BlockRule> rules,
        DateTime nowUtc,
        CancellationToken cancellationToken = default)
    {
        UpdateCalls++;
        LastRules = rules;
        LastNowUtc = nowUtc;
        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken = default)
    {
        StopCalls++;
        return Task.CompletedTask;
    }

    public void RaiseBlocked(string url, DateTimeOffset observedAtUtc) =>
        BlockedAttemptObserved?.Invoke(this, new BrowserBlockedAttempt(url, observedAtUtc));
}
