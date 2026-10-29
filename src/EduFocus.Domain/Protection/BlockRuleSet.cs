using EduFocus.Domain.Common;
using EduFocus.Domain.Entities;
using EduFocus.Domain.Enums;
using EduFocus.Domain.ValueObjects;

namespace EduFocus.Domain.Protection;

/// <summary>
/// Immutable snapshot of enforceable rules (BR-08, BR-09, BR-25). Build it once
/// from persisted rules and reuse it in hot paths so matching never queries the
/// database; rebuild the snapshot after rules change. Disabled and not-enforced
/// rules are dropped at construction, expiry is evaluated on every call.
/// </summary>
public sealed class BlockRuleSet
{
    private readonly BlockRule[] _rules;

    private BlockRuleSet(BlockRule[] rules) => _rules = rules;

    public int Count => _rules.Length;

    public static BlockRuleSet Create(IEnumerable<BlockRule> rules)
    {
        ArgumentNullException.ThrowIfNull(rules);

        var enforceable = rules
            .Where(rule => rule.IsEnabled && rule.EnforcementStatus == EnforcementStatus.Enforced)
            .ToArray();

        return new BlockRuleSet(enforceable);
    }

    public RuleDecision EvaluateDomain(DomainName domain, DateTime nowUtc)
    {
        ArgumentNullException.ThrowIfNull(domain);

        if (ProtectedDomains.IsProtected(domain))
            return RuleDecision.Allow;

        return BlockRuleEvaluator.EvaluateDomain(_rules, domain, nowUtc);
    }

    public RuleDecision EvaluateApp(string? processName, DateTime nowUtc)
    {
        Guard.Utc(nowUtc, nameof(nowUtc));
        return BlockRuleEvaluator.EvaluateApp(_rules, processName, nowUtc);
    }
}
