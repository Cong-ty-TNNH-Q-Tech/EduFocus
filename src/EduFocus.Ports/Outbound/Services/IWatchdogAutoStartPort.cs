using EduFocus.Ports.DTOs;

namespace EduFocus.Ports.Outbound.Services;

/// <summary>
/// Registers/unregisters the at-logon and one-minute health scheduled tasks.
/// Both run with the highest available privileges so the elevated app can be
/// restarted without a UAC prompt.
/// </summary>
public interface IWatchdogAutoStartPort
{
    Task SynchronizeAsync(
        WatchdogRegistrationMode mode,
        CancellationToken cancellationToken = default);
}
