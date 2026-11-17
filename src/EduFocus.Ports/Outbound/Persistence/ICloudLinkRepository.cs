using EduFocus.Domain.Entities;

namespace EduFocus.Ports.Outbound.Persistence;

/// <summary>Persists the singleton <see cref="CloudLink"/> row.</summary>
public interface ICloudLinkRepository
{
    Task<CloudLink?> GetAsync(CancellationToken cancellationToken = default);

    Task AddAsync(CloudLink link, CancellationToken cancellationToken = default);

    Task UpdateAsync(CloudLink link, CancellationToken cancellationToken = default);
}
