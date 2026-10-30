using EduFocus.Ports.DTOs;
using EduFocus.Ports.Inbound;

namespace EduFocus.Service.NoOps;

/// <summary>
/// A service in Session 0 cannot open the webcam. The attention pipeline stays
/// in the UI process; the protection state machine only sees this no-op.
/// </summary>
public sealed class NullAttentionPipelineUseCase : IAttentionPipelineUseCase
{
    public bool IsRunning => false;

    public event EventHandler<AttentionSample>? SampleProduced
    {
        add { }
        remove { }
    }

    public Task StartAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;

    public Task StopAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;

    public Task<AttentionSample> SampleOnceAsync(CancellationToken cancellationToken = default) =>
        Task.FromResult(
            new AttentionSample(
                DateTimeOffset.UtcNow,
                CameraFailureKind.NotStarted,
                Observation: null,
                Luminance: 0,
                CaptureMs: 0,
                DetectMs: 0,
                Error: "Attention monitoring runs in the EduFocus window, not in the service."));
}
