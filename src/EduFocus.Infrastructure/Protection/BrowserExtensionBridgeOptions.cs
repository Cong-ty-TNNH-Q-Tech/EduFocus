using EduFocus.Ports.Outbound.Services;

namespace EduFocus.Infrastructure.Protection;

public sealed class BrowserExtensionBridgeOptions
{
    public string PipeName { get; set; } = BrowserBridgeProtocol.PipeName;

    /// <summary>Chrome and Edge each start a host process, plus extra profiles.</summary>
    public int MaxClients { get; set; } = 8;
}
