using EduFocus.Domain.Entities;
using EduFocus.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace EduFocus.Infrastructure.Persistence;

public sealed class BlockRuleService(AppDbContext db)
{
    public async Task<IReadOnlyList<BlockRule>> GetRulesAsync(CancellationToken cancellationToken = default) =>
        await db.BlockRules.AsNoTracking().OrderBy(x => x.Category).ThenBy(x => x.NormalizedValue).ToListAsync(cancellationToken);

    public async Task SetEnabledAsync(Guid id, bool enabled, ParentModeSession parent, DateTimeOffset now,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(parent);
        if (now.Offset != TimeSpan.Zero) throw new ArgumentException("Timestamp must be UTC.", nameof(now));
        if (!parent.IsActive(now)) throw new UnauthorizedAccessException("An active Parent Mode session is required.");
        var rule = await db.BlockRules.SingleOrDefaultAsync(x => x.Id == id, cancellationToken)
            ?? throw new KeyNotFoundException("Block rule was not found.");
        if (!rule.IsBuiltIn) throw new InvalidOperationException("This view only manages built-in rules.");
        if (!enabled && rule.Effect == BlockRuleEffect.Allow && DefaultBlockRuleSeeder.IsEssentialAllowlist(rule.NormalizedValue))
            throw new InvalidOperationException("Essential online-learning allowlist entries cannot be disabled.");
        if (rule.IsEnabled == enabled) return;
        rule.SetEnabled(enabled, now);
        db.AuditLogs.Add(AuditLog.Create(now, AuditActor.Parent, enabled ? "BuiltInRuleEnabled" : "BuiltInRuleDisabled",
            $"Parent {(enabled ? "enabled" : "disabled")} a built-in {rule.RuleType} rule.", now));
        await db.SaveChangesAsync(cancellationToken);
    }
}
