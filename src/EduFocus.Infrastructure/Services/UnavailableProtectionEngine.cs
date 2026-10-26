using EduFocus.Domain.Entities;
using EduFocus.Ports.Outbound.Services;

namespace EduFocus.Infrastructure.Services;

/// <summary>Fails closed until the real operating-system adapters are registered.</summary>
public sealed class UnavailableProtectionEngine : IProtectionEnginePort
{
    public bool IsAvailable => false;

    public Task StartAsync(IReadOnlyCollection<BlockRule> rules, CancellationToken cancellationToken = default) =>
        throw new InvalidOperationException("The protection engine is unavailable; Protection was not enabled.");

    public Task StopAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;

    public Task ReconcileAsync(bool protectionEnabled, IReadOnlyCollection<BlockRule> rules, CancellationToken cancellationToken = default) =>
        protectionEnabled
            ? throw new InvalidOperationException("The protection engine is unavailable; saved Protection resources cannot be reconciled.")
            : Task.CompletedTask;
}
