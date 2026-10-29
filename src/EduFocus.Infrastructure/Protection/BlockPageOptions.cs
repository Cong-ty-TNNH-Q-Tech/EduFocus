namespace EduFocus.Infrastructure.Protection;

/// <summary>
/// Loopback block page settings. Production binds port 80 as required by the
/// hosts redirect; tests use port 0 to obtain an ephemeral port without
/// elevation.
/// </summary>
public sealed record BlockPageOptions
{
    public const int DefaultPort = 80;

    public int Port { get; init; } = DefaultPort;

    public int MaxHeaderBytes { get; init; } = 16 * 1024;

    public TimeSpan HeaderReadTimeout { get; init; } = TimeSpan.FromSeconds(5);

    public int MaxConcurrentConnections { get; init; } = 16;

    public int Backlog { get; init; } = 50;
}
