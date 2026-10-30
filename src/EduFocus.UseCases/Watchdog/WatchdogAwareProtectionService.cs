using EduFocus.Ports.DTOs;
using EduFocus.Ports.Inbound;

namespace EduFocus.UseCases.Watchdog;

/// <summary>
/// Keeps the watchdog in step with every protection transition (manual, schedule
/// or startup reconcile) without teaching the protection state machine about
/// processes or Task Scheduler. The coordinator itself is best-effort.
/// </summary>
public sealed class WatchdogAwareProtectionService : IProtectionUseCase
{
    private readonly IProtectionUseCase _inner;
    private readonly IWatchdogCoordinator _watchdog;

    public WatchdogAwareProtectionService(
        IProtectionUseCase inner,
        IWatchdogCoordinator watchdog)
    {
        _inner = inner ?? throw new ArgumentNullException(nameof(inner));
        _watchdog = watchdog ?? throw new ArgumentNullException(nameof(watchdog));
    }

    public Task<ProtectionStatus> GetStatusAsync(CancellationToken cancellationToken = default) =>
        _inner.GetStatusAsync(cancellationToken);

    public Task<ProtectionOperationResult> EnableAsync(CancellationToken cancellationToken = default) =>
        SynchronizeAfterAsync(_inner.EnableAsync(cancellationToken));

    public Task<ProtectionOperationResult> DisableAsync(CancellationToken cancellationToken = default) =>
        SynchronizeAfterAsync(_inner.DisableAsync(cancellationToken));

    public Task<ProtectionOperationResult> DisableForExitAsync(CancellationToken cancellationToken = default) =>
        SynchronizeAfterAsync(_inner.DisableForExitAsync(cancellationToken));

    public Task<ProtectionOperationResult> ReconcileAsync(CancellationToken cancellationToken = default) =>
        SynchronizeAfterAsync(_inner.ReconcileAsync(cancellationToken));

    public Task<ProtectionOperationResult> EnableForScheduleAsync(CancellationToken cancellationToken = default) =>
        SynchronizeAfterAsync(_inner.EnableForScheduleAsync(cancellationToken));

    public Task<ProtectionOperationResult> DisableForScheduleAsync(CancellationToken cancellationToken = default) =>
        SynchronizeAfterAsync(_inner.DisableForScheduleAsync(cancellationToken));

    public Task<ProtectionOperationResult> ReloadRulesAsync(CancellationToken cancellationToken = default) =>
        _inner.ReloadRulesAsync(cancellationToken);

    private async Task<ProtectionOperationResult> SynchronizeAfterAsync(
        Task<ProtectionOperationResult> operation)
    {
        var result = await operation;

        if (result.Changed)
            await _watchdog.SynchronizeAsync(CancellationToken.None);

        return result;
    }
}
