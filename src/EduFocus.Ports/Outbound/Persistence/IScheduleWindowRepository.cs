using EduFocus.Domain.Entities;
using EduFocus.Domain.ValueObjects;

namespace EduFocus.Ports.Outbound.Persistence;

/// <summary>
/// Weekly protection windows (database schema §7). Overlap validation belongs to
/// the domain; this port only persists whole windows.
/// </summary>
public interface IScheduleWindowRepository
{
    Task<IReadOnlyList<ScheduleWindow>> ListAsync(CancellationToken cancellationToken = default);

    Task<ScheduleWindow?> GetAsync(ScheduleWindowId id, CancellationToken cancellationToken = default);

    Task AddAsync(ScheduleWindow window, CancellationToken cancellationToken = default);

    Task UpdateAsync(ScheduleWindow window, CancellationToken cancellationToken = default);

    Task DeleteAsync(ScheduleWindowId id, CancellationToken cancellationToken = default);
}
