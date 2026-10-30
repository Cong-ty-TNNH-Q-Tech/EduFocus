using EduFocus.Ports.DTOs;

namespace EduFocus.Ports.Inbound;

/// <summary>
/// Samples the camera at the configured cadence, runs frame inference and
/// publishes one <see cref="AttentionSample"/> per tick. Each frame lives only
/// for the duration of a tick; no frame, landmark or embedding is retained.
/// </summary>
public interface IAttentionPipelineUseCase
{
    bool IsRunning { get; }

    event EventHandler<AttentionSample>? SampleProduced;

    Task StartAsync(CancellationToken cancellationToken = default);

    Task StopAsync(CancellationToken cancellationToken = default);

    Task<AttentionSample> SampleOnceAsync(CancellationToken cancellationToken = default);
}
