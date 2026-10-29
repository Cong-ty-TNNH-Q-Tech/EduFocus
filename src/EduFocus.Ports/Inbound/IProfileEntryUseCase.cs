using System.Threading.Tasks;
using EduFocus.Ports.DTOs;

namespace EduFocus.Ports.Inbound;

public interface IProfileEntryUseCase
{
    Task AddProfileEntryAsync(Guid profileId, ProfileEntryTypeDto type, string value, string? exePath = null);
    Task RemoveProfileEntryAsync(Guid profileId, Guid entryId);
    Task UpdateProfileEntryAsync(Guid profileId, Guid entryId, ProfileEntryTypeDto type, string value, string? exePath = null);
    Task ClearProfileEntriesAsync(Guid profileId);
}
