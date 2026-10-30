using EduFocus.Domain.ValueObjects;
using EduFocus.Ports.Inbound;

namespace EduFocus.UseCases.Tests.TestSupport;

internal sealed class FakeActivityUseCase : IActivityUseCase
{
    public List<StudySessionId> Started { get; } = [];

    public int StopCalls { get; private set; }

    public bool ThrowOnStart { get; set; }

    public bool IsRunning { get; private set; }

    public Task StartAsync(StudySessionId sessionId, CancellationToken cancellationToken = default)
    {
        if (ThrowOnStart)
            throw new InvalidOperationException("Activity tracking is unavailable.");

        Started.Add(sessionId);
        IsRunning = true;
        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken = default)
    {
        StopCalls++;
        IsRunning = false;
        return Task.CompletedTask;
    }

    public int RefreshSettingsCalls { get; private set; }

    public Task RefreshSettingsAsync(CancellationToken cancellationToken = default)
    {
        RefreshSettingsCalls++;
        return Task.CompletedTask;
    }

    public Task<bool> SampleOnceAsync(CancellationToken cancellationToken = default) =>
        Task.FromResult(false);
}
