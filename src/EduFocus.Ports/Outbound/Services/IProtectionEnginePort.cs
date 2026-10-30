namespace EduFocus.Ports.Outbound.Services;

/// <summary>
/// Applies and removes system-level protection (hosts entries, block page
/// listener and process watcher). Implementations must be idempotent and roll
/// back partial changes when a step fails (UC03, UC04; system architecture §6).
/// Non-fatal problems are returned as warnings so the UI can show a degraded
/// state instead of silently pretending everything is enforced.
/// </summary>
public interface IProtectionEnginePort
{
    Task<IReadOnlyList<string>> ApplyAsync(CancellationToken cancellationToken = default);

    Task<IReadOnlyList<string>> RemoveAsync(CancellationToken cancellationToken = default);

    Task<IReadOnlyList<string>> ReconcileAsync(
        bool shouldBeActive,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<string>> ReloadRulesAsync(CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<string>>([]);
}
