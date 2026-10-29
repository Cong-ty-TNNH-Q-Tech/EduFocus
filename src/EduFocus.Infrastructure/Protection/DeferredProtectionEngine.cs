using EduFocus.Ports.Outbound.Services;

namespace EduFocus.Infrastructure.Protection;

/// <summary>
/// Temporary protection engine used while the system-level blocking adapters
/// are not implemented (Phase 3, issue #24). It never touches hosts, listeners
/// or processes; it returns an explicit warning so the UI does not claim that
/// websites and apps are being blocked yet.
/// </summary>
public sealed class DeferredProtectionEngine : IProtectionEnginePort
{
    public const string NotAppliedWarning =
        "Chưa áp dụng chặn web/app: engine bảo vệ sẽ được bật trong Phase 3.";

    private readonly IAppLogger _logger;

    public DeferredProtectionEngine(IAppLogger logger)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public Task<IReadOnlyList<string>> ApplyAsync(CancellationToken cancellationToken = default)
    {
        _logger.Warning(LogCategories.Protection, NotAppliedWarning);
        IReadOnlyList<string> warnings = [NotAppliedWarning];
        return Task.FromResult(warnings);
    }

    public Task<IReadOnlyList<string>> RemoveAsync(CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<string>>([]);

    public Task<IReadOnlyList<string>> ReconcileAsync(
        bool shouldBeActive,
        CancellationToken cancellationToken = default) =>
        shouldBeActive
            ? ApplyAsync(cancellationToken)
            : Task.FromResult<IReadOnlyList<string>>([]);
}
