using EduFocus.Ports.DTOs;

namespace EduFocus.Ports.Inbound;

/// <summary>
/// Parent Mode blocklist management (UC05). Both operations require an active
/// Parent Mode session; the UI never toggles rules directly through the repository.
/// </summary>
public interface IBlockRuleUseCase
{
    Task<IReadOnlyList<BlockRuleInfo>> ListAsync(CancellationToken cancellationToken = default);

    Task<BlockRuleOperationResult> SetEnabledAsync(
        Guid id,
        bool isEnabled,
        CancellationToken cancellationToken = default);

    Task<BlockRuleOperationResult> AddCustomAsync(
        CreateBlockRuleRequest request,
        CancellationToken cancellationToken = default);

    Task<bool> DeleteCustomAsync(
        Guid id,
        CancellationToken cancellationToken = default);
}
