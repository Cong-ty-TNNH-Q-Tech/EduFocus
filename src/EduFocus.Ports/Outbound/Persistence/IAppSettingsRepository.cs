using EduFocus.Domain.Entities;

namespace EduFocus.Ports.Outbound.Persistence;

/// <summary>Singleton application settings row (database schema §4).</summary>
public interface IAppSettingsRepository
{
    Task<AppSettings?> GetAsync(CancellationToken cancellationToken = default);

    Task AddAsync(AppSettings settings, CancellationToken cancellationToken = default);

    Task UpdateAsync(AppSettings settings, CancellationToken cancellationToken = default);
}
