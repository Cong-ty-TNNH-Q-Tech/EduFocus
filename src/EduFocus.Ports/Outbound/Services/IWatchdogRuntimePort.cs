namespace EduFocus.Ports.Outbound.Services;

/// <summary>
/// App-side control of the resident watchdog process. Stops are signalled
/// through a named event and confirmed by waiting for the watchdog mutex.
/// </summary>
public interface IWatchdogRuntimePort
{
    bool IsRunning { get; }

    /// <summary>Starts <c>EduFocus.exe --watchdog</c> when protection is ON.</summary>
    bool EnsureStarted();

    /// <summary>Lets the watchdog finish the current tick without restarting the app.</summary>
    void NotifySessionEnding();

    Task<bool> RequestStopAsync(TimeSpan timeout, CancellationToken cancellationToken = default);
}
