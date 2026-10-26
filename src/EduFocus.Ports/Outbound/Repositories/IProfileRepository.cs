using System.Collections.Generic;
using System.Threading.Tasks;
using EduFocus.Domain.Entities;
using EduFocus.Domain.ValueObjects;

namespace EduFocus.Ports.Outbound.Repositories;

public interface IProfileRepository
{
    Task<IReadOnlyCollection<Profile>> GetAllAsync();
    Task<Profile?> GetByIdAsync(ProfileId id);
    Task<Profile?> GetDefaultProfileAsync();
    
    /// <summary>
    /// Seeds default profile if none exists. Call once at application startup.
    /// </summary>
    Task EnsureDefaultProfileAsync();
    
    Task AddAsync(Profile profile);
    Task UpdateAsync(Profile profile);
    Task DeleteAsync(ProfileId id);
}
