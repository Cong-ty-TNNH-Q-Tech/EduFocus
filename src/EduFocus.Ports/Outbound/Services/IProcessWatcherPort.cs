using EduFocus.Domain.Entities;

namespace EduFocus.Ports.Outbound.Services;

public interface IProcessWatcherPort
{
    bool IsRunning { get; }
    Task StartAsync(IReadOnlyCollection<BlockRule> rules, CancellationToken cancellationToken = default);
    Task UpdateRulesAsync(IReadOnlyCollection<BlockRule> rules, CancellationToken cancellationToken = default);
    Task StopAsync(CancellationToken cancellationToken = default);
}
