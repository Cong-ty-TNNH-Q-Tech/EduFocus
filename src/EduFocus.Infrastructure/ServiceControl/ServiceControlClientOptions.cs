using EduFocus.Ports.ServiceControl;

namespace EduFocus.Infrastructure.ServiceControl;

public sealed record ServiceControlClientOptions
{
    public string PipeName { get; init; } = ServiceControlProtocol.PipeName;

    public TimeSpan ConnectTimeout { get; init; } = ServiceControlProtocol.DefaultConnectTimeout;

    public TimeSpan RequestTimeout { get; init; } = ServiceControlProtocol.DefaultRequestTimeout;

    /// <summary>
    /// Full path of the installed service image. When set, the client refuses
    /// to send commands to a squatted pipe server. Tests may leave it null.
    /// </summary>
    public string? ExpectedServerImagePath { get; init; }

    /// <summary>
    /// Manual protection and exit commands require an active Parent Mode
    /// session. Health checks and schedule evaluation do not.
    /// </summary>
    public bool RequireParentAuthorization { get; init; } = true;
}
