using EduFocus.Ports.Outbound.Services;
using EduFocus.Ports.Protection;

namespace EduFocus.UseCases.Tests.TestSupport;

internal sealed class FakeProtectionEngine : IProtectionEnginePort
{
    public int ApplyCount { get; private set; }

    public int RemoveCount { get; private set; }

    public int ReconcileCount { get; private set; }

    public bool? LastReconcileActive { get; private set; }

    public bool ThrowOnApply { get; set; }

    public bool ThrowOnRemove { get; set; }

    public bool ThrowOnReconcile { get; set; }

    public TaskCompletionSource? ApplyGate { get; set; }

    public IReadOnlyList<string> Warnings { get; set; } = [];

    public async Task<IReadOnlyList<string>> ApplyAsync(CancellationToken cancellationToken = default)
    {
        ApplyCount++;

        if (ApplyGate is { } gate)
            await gate.Task;

        if (ThrowOnApply)
            throw new ProtectionEngineException("apply", "Blocking adapter failed.");

        return Warnings;
    }

    public Task<IReadOnlyList<string>> RemoveAsync(CancellationToken cancellationToken = default)
    {
        RemoveCount++;

        if (ThrowOnRemove)
            throw new ProtectionEngineException("remove", "Blocking adapter failed.");

        return Task.FromResult(Warnings);
    }

    public Task<IReadOnlyList<string>> ReconcileAsync(
        bool shouldBeActive,
        CancellationToken cancellationToken = default)
    {
        ReconcileCount++;
        LastReconcileActive = shouldBeActive;

        if (ThrowOnReconcile)
            throw new ProtectionEngineException("reconcile", "Blocking adapter failed.");

        return Task.FromResult(Warnings);
    }
}
