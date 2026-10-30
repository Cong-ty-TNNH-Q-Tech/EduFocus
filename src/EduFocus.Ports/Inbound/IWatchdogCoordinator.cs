namespace EduFocus.Ports.Inbound;

/// <summary>
/// Keeps the watchdog process and its scheduled tasks in sync with the
/// persisted onboarding/protection state. Always best-effort: a watchdog
/// failure must never block protection, onboarding or a clean exit.
/// </summary>
public interface IWatchdogCoordinator
{
    Task SynchronizeAsync(CancellationToken cancellationToken = default);
}
