using EduFocus.Domain.ValueObjects;
using EduFocus.Ports.Inbound;

namespace EduFocus.UseCases.Tests.TestSupport;

internal sealed class FakeStudySessionHeartbeatUseCase : IStudySessionHeartbeatUseCase
{
    public List<StudySessionId> StartedSessions { get; } = [];

    public int StopCount { get; private set; }

    public bool ThrowOnStart { get; set; }

    public bool IsRunning { get; private set; }

    public Task StartAsync(
        StudySessionId sessionId,
        DateTime lastHeartbeatAtUtc,
        CancellationToken cancellationToken = default)
    {
        if (ThrowOnStart)
            throw new InvalidOperationException("The heartbeat could not start.");

        StartedSessions.Add(sessionId);
        IsRunning = true;
        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken = default)
    {
        StopCount++;
        IsRunning = false;
        return Task.CompletedTask;
    }

    public Task<bool> BeatOnceAsync(CancellationToken cancellationToken = default) =>
        Task.FromResult(IsRunning);
}
