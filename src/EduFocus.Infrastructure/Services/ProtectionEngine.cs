using EduFocus.Domain.Entities;
using EduFocus.Ports.Outbound.Services;

namespace EduFocus.Infrastructure.Services;

/// <summary>Coordinates the concrete enforcement adapters while allowing the block page to degrade independently.</summary>
public sealed class ProtectionEngine(
    IHostsFilePort hosts,
    IBlockPagePort blockPage,
    IProcessWatcherPort processWatcher,
    IAppLogger logger) : IProtectionEnginePort
{
    private readonly SemaphoreSlim _gate = new(1, 1);
    private IReadOnlyCollection<BlockRule> _rules = Array.Empty<BlockRule>();
    private bool _active;

    public bool IsAvailable => true;
    public bool IsBlockPageAvailable => blockPage.IsAvailable;

    public async Task StartAsync(IReadOnlyCollection<BlockRule> rules, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(rules);
        await _gate.WaitAsync(cancellationToken);
        try
        {
            var nextRules = rules.ToArray();
            if (!_active)
            {
                await StartAdaptersAsync(nextRules, cancellationToken);
                _rules = nextRules;
                _active = true;
                return;
            }

            var previous = _rules;
            try
            {
                await hosts.ApplyAsync(nextRules, DateTimeOffset.UtcNow, cancellationToken);
                if (!await blockPage.StartAsync(nextRules, cancellationToken))
                    logger.Warning("Protection is active, but the local block page is unavailable.");
                await processWatcher.StartAsync(nextRules, cancellationToken);
                _rules = nextRules;
            }
            catch
            {
                await TryRestoreRulesAsync(previous, cancellationToken);
                throw;
            }
        }
        finally { _gate.Release(); }
    }

    public async Task StopAsync(CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            var wasActive = _active;
            var previous = _rules;
            var errors = new List<Exception>();
            await TryAdapterAsync(() => processWatcher.StopAsync(cancellationToken), errors);
            await TryAdapterAsync(() => blockPage.StopAsync(cancellationToken), errors);
            await TryAdapterAsync(() => hosts.RestoreAsync(cancellationToken), errors);
            if (errors.Count > 0)
            {
                if (wasActive)
                {
                    try { await StartAdaptersAsync(previous, cancellationToken); }
                    catch (Exception recoveryError) { logger.Error("Protection resources could not be fully restored after a failed stop.", recoveryError); }
                }
                throw new AggregateException("One or more protection adapters could not be stopped safely.", errors);
            }
            _rules = Array.Empty<BlockRule>();
            _active = false;
        }
        finally { _gate.Release(); }
    }

    public Task ReconcileAsync(bool protectionEnabled, IReadOnlyCollection<BlockRule> rules, CancellationToken cancellationToken = default) =>
        protectionEnabled ? StartAsync(rules, cancellationToken) : StopAsync(cancellationToken);

    private async Task StartAdaptersAsync(IReadOnlyCollection<BlockRule> rules, CancellationToken cancellationToken)
    {
        try
        {
            await hosts.ApplyAsync(rules, DateTimeOffset.UtcNow, cancellationToken);
            if (!await blockPage.StartAsync(rules, cancellationToken))
                logger.Warning("Protection is active, but the local block page could not bind; hosts enforcement remains active.");
            await processWatcher.StartAsync(rules, cancellationToken);
        }
        catch
        {
            var cleanup = new List<Exception>();
            await TryAdapterAsync(() => processWatcher.StopAsync(CancellationToken.None), cleanup);
            await TryAdapterAsync(() => blockPage.StopAsync(CancellationToken.None), cleanup);
            await TryAdapterAsync(() => hosts.RestoreAsync(CancellationToken.None), cleanup);
            foreach (var error in cleanup) logger.Error("Protection adapter rollback was incomplete.", error);
            throw;
        }
    }

    private async Task TryRestoreRulesAsync(IReadOnlyCollection<BlockRule> rules, CancellationToken cancellationToken)
    {
        var failures = new List<Exception>();
        await TryAdapterAsync(() => hosts.ApplyAsync(rules, DateTimeOffset.UtcNow, cancellationToken), failures);
        await TryAdapterAsync(async () => { await blockPage.StartAsync(rules, cancellationToken); }, failures);
        await TryAdapterAsync(() => processWatcher.StartAsync(rules, cancellationToken), failures);
        foreach (var failure in failures) logger.Error("Protection rule update rollback was incomplete.", failure);
    }

    private static async Task TryAdapterAsync(Func<Task> operation, List<Exception> errors)
    {
        try { await operation(); }
        catch (Exception ex) { errors.Add(ex); }
    }
}
