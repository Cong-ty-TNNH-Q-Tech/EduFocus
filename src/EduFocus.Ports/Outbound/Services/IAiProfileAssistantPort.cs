using System.Threading.Tasks;

namespace EduFocus.Ports.Outbound.Services;

public interface IAiProfileAssistantPort
{
    Task<string> GenerateSmartProfileAsync(string intent);
}
