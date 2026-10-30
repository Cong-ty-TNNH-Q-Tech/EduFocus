using EduFocus.Ports.DTOs;
using EduFocus.Ports.Inbound;

namespace EduFocus.UseCases.Tests.TestSupport;

internal sealed class FakeAttentionPipelineUseCase : IAttentionPipelineUseCase
{
    public bool IsRunning { get; private set; }

    public int StartCalls { get; private set; }

    public int StopCalls { get; private set; }

    public bool ThrowOnStart { get; set; }

    public bool ThrowOnStop { get; set; }

    public event EventHandler<AttentionSample>? SampleProduced;

    public Task StartAsync(CancellationToken cancellationToken = default)
    {
        if (ThrowOnStart)
            throw new InvalidOperationException("The camera pipeline is unavailable.");

        StartCalls++;
        IsRunning = true;
        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken = default)
    {
        StopCalls++;

        if (ThrowOnStop)
            throw new InvalidOperationException("The camera pipeline cannot stop.");

        IsRunning = false;
        return Task.CompletedTask;
    }

    public Task<AttentionSample> SampleOnceAsync(CancellationToken cancellationToken = default) =>
        throw new NotSupportedException("The fake pipeline does not sample on demand.");

    public void Raise(AttentionSample sample) => SampleProduced?.Invoke(this, sample);
}
