namespace EduFocus.Ports.Outbound.Services;

/// <summary>
/// A blocked navigation observed by the local block page. The engine debounces
/// and records these as BlockedAttempt rows (ObservedVia = BlockPage, target
/// type Domain); hosts blocking itself cannot observe HTTPS attempts.
/// </summary>
public sealed record BlockPageHit(DateTimeOffset ObservedAtUtc, string Domain);

/// <summary>
/// Result of starting the loopback block page. Bind failures are non-fatal:
/// the engine keeps Protection ON and surfaces <see cref="Warning"/> to the UI.
/// </summary>
public sealed record BlockPageStartResult(bool Started, int Port, string? Warning);

/// <summary>
/// Serves the static block page on loopback for domains redirected by the
/// hosts file (system architecture §6.2). Implementations are idempotent,
/// never load external resources and never throw on port conflicts.
/// </summary>
public interface IBlockPageServer
{
    bool IsRunning { get; }

    int Port { get; }

    Task<BlockPageStartResult> StartAsync(
        IReadOnlyCollection<string> blockedDomains,
        CancellationToken cancellationToken = default);

    void UpdateDomains(IReadOnlyCollection<string> blockedDomains);

    Task StopAsync(CancellationToken cancellationToken = default);

    event Action<BlockPageHit>? BlockedRequestObserved;
}
