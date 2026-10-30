using EduFocus.Ports.ServiceControl;

namespace EduFocus.Infrastructure.ServiceControl;

public sealed record ServiceControlServerOptions
{
    public string PipeName { get; init; } = ServiceControlProtocol.PipeName;

    public int MaxClients { get; init; } = 4;

    public TimeSpan RequestTimeout { get; init; } = TimeSpan.FromSeconds(30);

    /// <summary>
    /// Full path of the only executable allowed to connect. The installer
    /// places <c>EduFocus.exe</c> next to the service, so this is normally
    /// <c>AppContext.BaseDirectory\EduFocus.exe</c>.
    /// </summary>
    public string? ExpectedClientImagePath { get; init; }

    /// <summary>
    /// Adds the current user to the pipe ACL. The Windows service runs as
    /// SYSTEM and does not need it; console mode and integration tests do.
    /// </summary>
    public bool AllowCurrentUser { get; init; }
}
