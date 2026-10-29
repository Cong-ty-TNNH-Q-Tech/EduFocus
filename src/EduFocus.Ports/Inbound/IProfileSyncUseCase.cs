using System.Threading.Tasks;

namespace EduFocus.Ports.Inbound;

public interface IProfileSyncUseCase
{
    Task<string> ExportProfilesAsync();
    Task ImportProfilesAsync(string jsonContent, bool overwriteExisting);
}
