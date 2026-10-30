using EduFocus.Ports.DTOs;

namespace EduFocus.Ports.Inbound;

/// <summary>
/// Installs the built-in block/allow rules at startup (UC05). Seeding is
/// idempotent: it adds missing rules, never overwrites parent-created rules and
/// preserves the enabled/expiry state of existing built-in rules.
/// </summary>
public interface IBlockRuleSeedUseCase
{
    Task<BlockRuleSeedResult> EnsureSeededAsync(CancellationToken cancellationToken = default);
}
