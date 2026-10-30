using EduFocus.Ports.DTOs;

namespace EduFocus.Ports.Outbound.Services;

/// <summary>Liveness beacon written by the app and read by the watchdog.</summary>
public interface IWatchdogHeartbeatStore
{
    Task<WatchdogHeartbeat?> ReadAsync(CancellationToken cancellationToken = default);

    Task WriteAsync(WatchdogHeartbeat heartbeat, CancellationToken cancellationToken = default);

    Task DeleteAsync(CancellationToken cancellationToken = default);
}
