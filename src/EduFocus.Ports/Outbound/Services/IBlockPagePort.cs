using EduFocus.Domain.Entities;

namespace EduFocus.Ports.Outbound.Services;

public interface IBlockPagePort
{
    bool IsAvailable { get; }
    Task<bool> StartAsync(IReadOnlyCollection<BlockRule> rules, CancellationToken cancellationToken = default);
    Task UpdateRulesAsync(IReadOnlyCollection<BlockRule> rules, CancellationToken cancellationToken = default);
    Task StopAsync(CancellationToken cancellationToken = default);
}
