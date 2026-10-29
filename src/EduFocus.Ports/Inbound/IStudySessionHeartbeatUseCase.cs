using EduFocus.Domain.ValueObjects;

namespace EduFocus.Ports.Inbound;

/// <summary>
/// Drives the 10-second StudySession heartbeat independently of activity
/// sampling (UC07). Active time is measured with the monotonic clock, so a
/// wall-clock change never creates negative or inflated durations. The
/// heartbeat stops before a session is completed, aborted or interrupted.
/// </summary>
public interface IStudySessionHeartbeatUseCase
{
    /// <summary>True while a background heartbeat loop is running.</summary>
    bool IsRunning { get; }

    /// <summary>
    /// Starts the heartbeat loop for a session whose latest persisted heartbeat
    /// is <paramref name="lastHeartbeatAtUtc"/>. Starting again replaces any
    /// previous loop after flushing a final beat.
    /// </summary>
    Task StartAsync(
        StudySessionId sessionId,
        DateTime lastHeartbeatAtUtc,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Stops the loop and flushes one final heartbeat so the session ends near
    /// its real stop time. Session termination always calls this first.
    /// </summary>
    Task StopAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Writes one heartbeat immediately. Returns false when there is no running
    /// session or the persisted session is no longer Active; transient write
    /// failures are logged and retried by the next beat.
    /// </summary>
    Task<bool> BeatOnceAsync(CancellationToken cancellationToken = default);
}
