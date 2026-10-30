using EduFocus.Domain.Attention;
using EduFocus.Domain.ValueObjects;
using EduFocus.Ports.DTOs;

namespace EduFocus.Ports.Inbound;

/// <summary>
/// Consumes attention samples, smooths them with a sliding window, runs the
/// Focused/Distracted/Absent/Drowsy/CameraUnavailable state machine and keeps
/// one open AttentionEvent per change. Session wiring (starting the camera
/// pipeline and this monitor together) belongs to issue #33.
/// </summary>
public interface IAttentionMonitorUseCase
{
    bool IsRunning { get; }

    AttentionState? CurrentState { get; }

    Task StartAsync(StudySessionId sessionId, CancellationToken cancellationToken = default);

    Task StopAsync(CancellationToken cancellationToken = default);

    Task RefreshSettingsAsync(CancellationToken cancellationToken = default) =>
        Task.CompletedTask;

    /// <summary>
    /// Queues one sample and waits until it has been classified and persisted.
    /// Returns false when the monitor is not running or the session ended.
    /// </summary>
    Task<bool> ProcessAsync(AttentionSample sample, CancellationToken cancellationToken = default);
}
