using EduFocus.Domain.ValueObjects;
using EduFocus.Ports.Inbound;

namespace EduFocus.Service.NoOps;

/// <summary>
/// Foreground activity sampling needs the interactive user session, which a
/// Windows service cannot observe. The UI owns it in service mode; the
/// protection state machine only sees this no-op.
/// </summary>
public sealed class NullActivityUseCase : IActivityUseCase
{
    public bool IsRunning => false;

    public Task StartAsync(StudySessionId sessionId, CancellationToken cancellationToken = default) =>
        Task.CompletedTask;

    public Task StopAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;

    public Task RefreshSettingsAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;

    public Task<bool> SampleOnceAsync(CancellationToken cancellationToken = default) =>
        Task.FromResult(false);
}
