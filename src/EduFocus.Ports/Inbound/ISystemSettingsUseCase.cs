using System.Threading.Tasks;
using EduFocus.Ports.DTOs;

namespace EduFocus.Ports.Inbound;

public interface ISystemSettingsUseCase
{
    Task<SystemSettingsDto> GetSettingsAsync();
    Task UpdateSettingsAsync(bool globalStrictMode, bool enableQuotes, string quoteLanguage, bool darkTheme,
        string? geminiApiKey = null, string geminiModel = "gemini-2.5-flash");
}
