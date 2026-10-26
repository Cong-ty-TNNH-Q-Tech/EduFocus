using System.Collections.Generic;
using System.Threading.Tasks;
using EduFocus.Ports.DTOs;

namespace EduFocus.Ports.Inbound;

public interface IProfileUseCase
{
    Task<IReadOnlyCollection<ProfileDto>> GetAllProfilesAsync();
    Task<ProfileDto> GetProfileByIdAsync(Guid profileId);
    Task<ProfileDto> GetDefaultProfileAsync();
    Task<ProfileDto> CreateProfileAsync(string name, bool isDefault);
    Task UpdateProfileNameAsync(Guid profileId, string newName);
    Task DeleteProfileAsync(Guid profileId);
    Task<string> AiGenerateProfileSuggestionAsync(string intent);
}
