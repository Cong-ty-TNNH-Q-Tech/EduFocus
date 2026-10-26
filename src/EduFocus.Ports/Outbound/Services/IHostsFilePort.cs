using EduFocus.Domain.Entities;

namespace EduFocus.Ports.Outbound.Services;

public interface IHostsFilePort
{
    Task ApplyAsync(IReadOnlyCollection<BlockRule> rules, DateTimeOffset now, CancellationToken cancellationToken = default);
    Task RestoreAsync(CancellationToken cancellationToken = default);
}
