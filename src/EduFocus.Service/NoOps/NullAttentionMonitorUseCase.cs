using EduFocus.Domain.Attention;
using EduFocus.Domain.ValueObjects;
using EduFocus.Ports.DTOs;
using EduFocus.Ports.Inbound;

namespace EduFocus.Service.NoOps;

/// <summary>
/// Attention classification runs next to the camera in the UI process, so the
/// service registers this no-op to keep the protection state machine intact.
/// </summary>
public sealed class NullAttentionMonitorUseCase : IAttentionMonitorUseCase
{
    public bool IsRunning => false;

    public AttentionState? CurrentState => null;

    public Task StartAsync(StudySessionId sessionId, CancellationToken cancellationToken = default) =>
        Task.CompletedTask;

    public Task StopAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;

    public Task<bool> ProcessAsync(AttentionSample sample, CancellationToken cancellationToken = default) =>
        Task.FromResult(false);
}
