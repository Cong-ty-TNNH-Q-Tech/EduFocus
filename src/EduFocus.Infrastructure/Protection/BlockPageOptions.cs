namespace EduFocus.Infrastructure.Protection;

/// <summary>
/// Loopback block page settings. Production binds port 80 and port 443 as
/// required by the hosts redirect; tests use port 0 to obtain ephemeral ports
/// without elevation. The HTTPS listener only starts when a trusted local
/// certificate authority is available.
/// </summary>
public sealed record BlockPageOptions
{
    public const int DefaultPort = 80;
    public const int DefaultHttpsPort = 443;

    public int Port { get; init; } = DefaultPort;

    public int HttpsPort { get; init; } = DefaultHttpsPort;

    public int MaxHeaderBytes { get; init; } = 16 * 1024;

    public TimeSpan HeaderReadTimeout { get; init; } = TimeSpan.FromSeconds(5);

    public int MaxConcurrentConnections { get; init; } = 16;

    public int Backlog { get; init; } = 50;
}
