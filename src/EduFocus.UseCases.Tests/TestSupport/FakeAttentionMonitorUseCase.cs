using EduFocus.Domain.Attention;
using EduFocus.Domain.ValueObjects;
using EduFocus.Ports.DTOs;
using EduFocus.Ports.Inbound;

namespace EduFocus.UseCases.Tests.TestSupport;

internal sealed class FakeAttentionMonitorUseCase : IAttentionMonitorUseCase
{
    public List<StudySessionId> Started { get; } = [];

    public int StopCalls { get; private set; }

    public bool ThrowOnStart { get; set; }

    public bool ThrowOnStop { get; set; }

    public bool IsRunning { get; private set; }

    public AttentionState? CurrentState { get; private set; }

    public Task StartAsync(StudySessionId sessionId, CancellationToken cancellationToken = default)
    {
        if (ThrowOnStart)
            throw new InvalidOperationException("Attention monitoring is unavailable.");

        Started.Add(sessionId);
        IsRunning = true;
        CurrentState = AttentionState.Focused;
        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken = default)
    {
        StopCalls++;

        if (ThrowOnStop)
            throw new InvalidOperationException("Attention monitoring cannot stop.");

        IsRunning = false;
        CurrentState = null;
        return Task.CompletedTask;
    }

    public Task<bool> ProcessAsync(AttentionSample sample, CancellationToken cancellationToken = default) =>
        Task.FromResult(false);
}
