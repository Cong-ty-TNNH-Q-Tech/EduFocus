using EduFocus.Ports.DTOs;
using EduFocus.Ports.Inbound;

namespace EduFocus.UseCases.Protection;

/// <summary>
/// Service-mode <see cref="IProtectionUseCase"/>: every command goes to the
/// Windows service, and the session-bound monitoring overlay is aligned around
/// it. A stop-then-command order guarantees the camera is released before the
/// service completes the session; a failed command resumes monitoring from the
/// session the service actually kept.
/// </summary>
public sealed class ServiceMonitoredProtectionUseCase : IProtectionUseCase
{
    private readonly IProtectionUseCase _inner;
    private readonly ServiceMonitoringCoordinator _monitoring;

    public ServiceMonitoredProtectionUseCase(
        IProtectionUseCase inner,
        ServiceMonitoringCoordinator monitoring)
    {
        _inner = inner ?? throw new ArgumentNullException(nameof(inner));
        _monitoring = monitoring ?? throw new ArgumentNullException(nameof(monitoring));
    }

    public Task<ProtectionStatus> GetStatusAsync(CancellationToken cancellationToken = default) =>
        _inner.GetStatusAsync(cancellationToken);

    public Task<ProtectionOperationResult> EnableAsync(CancellationToken cancellationToken = default) =>
        ApplyAsync(() => _inner.EnableAsync(cancellationToken));

    public Task<ProtectionOperationResult> DisableAsync(CancellationToken cancellationToken = default) =>
        RemoveAsync(() => _inner.DisableAsync(cancellationToken));

    public Task<ProtectionOperationResult> DisableForExitAsync(CancellationToken cancellationToken = default) =>
        RemoveAsync(() => _inner.DisableForExitAsync(cancellationToken));

    public Task<ProtectionOperationResult> ReconcileAsync(CancellationToken cancellationToken = default) =>
        ApplyAsync(() => _inner.ReconcileAsync(cancellationToken));

    public Task<ProtectionOperationResult> EnableForScheduleAsync(CancellationToken cancellationToken = default) =>
        ApplyAsync(() => _inner.EnableForScheduleAsync(cancellationToken));

    public Task<ProtectionOperationResult> DisableForScheduleAsync(CancellationToken cancellationToken = default) =>
        RemoveAsync(() => _inner.DisableForScheduleAsync(cancellationToken));

    public Task<ProtectionOperationResult> ReloadRulesAsync(CancellationToken cancellationToken = default) =>
        _inner.ReloadRulesAsync(cancellationToken);

    private async Task<ProtectionOperationResult> ApplyAsync(
        Func<Task<ProtectionOperationResult>> operation)
    {
        var result = await operation();
        await _monitoring.SyncAsync(result.Status, CancellationToken.None);
        return result;
    }

    private async Task<ProtectionOperationResult> RemoveAsync(
        Func<Task<ProtectionOperationResult>> operation)
    {
        await _monitoring.StopAsync(CancellationToken.None);

        try
        {
            return await operation();
        }
        catch
        {
            await _monitoring.RestoreAsync(CancellationToken.None);
            throw;
        }
    }
}
