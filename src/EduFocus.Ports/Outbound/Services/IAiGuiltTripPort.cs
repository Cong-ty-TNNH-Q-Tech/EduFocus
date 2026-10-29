using System.Threading.Tasks;

namespace EduFocus.Ports.Outbound.Services;

public interface IAiGuiltTripPort
{
    Task<string> GenerateGuiltTripQuoteAsync(string taskName, string language);
}
