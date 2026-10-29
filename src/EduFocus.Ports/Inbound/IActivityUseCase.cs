using EduFocus.Domain.ValueObjects;

namespace EduFocus.Ports.Inbound;

/// <summary>
/// Periodic foreground-activity sampling for the active StudySession (UC07,
/// issue #26). Sampling runs independently from the 10-second heartbeat: it
/// writes ActivitySample rows and category totals while the session is Active.
/// </summary>
public interface IActivityUseCase
{
    bool IsRunning { get; }

    /// <summary>
    /// Starts sampling for <paramref name="sessionId"/>. Calling it while a
    /// previous loop is running replaces that loop.
    /// </summary>
    Task StartAsync(StudySessionId sessionId, CancellationToken cancellationToken = default);

    /// <summary>Stops the sampling loop. Safe to call when it is not running.</summary>
    Task StopAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Re-reads monitoring coverage and the sample cadence from AppSettings so
    /// a Parent Mode change applies to the running session. A read failure
    /// keeps the previous values.
    /// </summary>
    Task RefreshSettingsAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Captures and persists one sample when the current cadence is due.
    /// Returns true when a row was written. Exposed for deterministic tests and
    /// for hosts that do not run the background loop.
    /// </summary>
    Task<bool> SampleOnceAsync(CancellationToken cancellationToken = default);
}
