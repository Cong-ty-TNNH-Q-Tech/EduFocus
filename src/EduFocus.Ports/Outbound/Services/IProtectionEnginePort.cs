using EduFocus.Domain.Entities;

namespace EduFocus.Ports.Outbound.Services;

/// <summary>Coordinates the operating-system resources that enforce the currently enabled block rules.</summary>
public interface IProtectionEnginePort
{
    bool IsAvailable { get; }
    bool IsBlockPageAvailable => true;
    Task StartAsync(IReadOnlyCollection<BlockRule> rules, CancellationToken cancellationToken = default);
    Task StopAsync(CancellationToken cancellationToken = default);
    Task ReconcileAsync(bool protectionEnabled, IReadOnlyCollection<BlockRule> rules, CancellationToken cancellationToken = default);
}
