using EduFocus.Ports.DTOs;

namespace EduFocus.Ports.Outbound.Services;

/// <summary>
/// Durable watchdog state (restart budget, pending start, give-up cooldown) so a
/// watchdog restart cannot bypass the loop protection.
/// </summary>
public interface IWatchdogStateStore
{
    Task<WatchdogRestartState?> ReadAsync(CancellationToken cancellationToken = default);

    Task WriteAsync(WatchdogRestartState state, CancellationToken cancellationToken = default);

    Task DeleteAsync(CancellationToken cancellationToken = default);
}
