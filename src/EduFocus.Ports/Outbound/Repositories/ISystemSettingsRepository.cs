using System.Threading.Tasks;
using EduFocus.Domain.Entities;

namespace EduFocus.Ports.Outbound.Repositories;

public interface ISystemSettingsRepository
{
    Task<SystemSettings> GetSettingsAsync();
    Task UpdateSettingsAsync(SystemSettings settings);

    /// <summary>
    /// Seeds default settings if none exist. Call once at application startup before any UseCase runs.
    /// </summary>
    Task EnsureDefaultSettingsAsync();
}
