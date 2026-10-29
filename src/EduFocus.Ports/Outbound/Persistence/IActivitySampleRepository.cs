using EduFocus.Domain.Entities;

namespace EduFocus.Ports.Outbound.Persistence;

/// <summary>Foreground activity samples (database schema §9).</summary>
public interface IActivitySampleRepository
{
    Task AddAsync(ActivitySample sample, CancellationToken cancellationToken = default);
}
