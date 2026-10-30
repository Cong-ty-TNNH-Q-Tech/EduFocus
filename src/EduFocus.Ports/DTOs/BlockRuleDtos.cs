using EduFocus.Domain.Enums;

namespace EduFocus.Ports.DTOs;

/// <summary>A block or allow rule projected for the Parent Mode blocklist (UC05).</summary>
public sealed record BlockRuleInfo(
    Guid Id,
    RuleType RuleType,
    RuleMatchType MatchType,
    BlockRuleCategory Category,
    BlockRuleEffect Effect,
    string Value,
    bool IsBuiltIn,
    bool IsEnabled,
    EnforcementStatus EnforcementStatus,
    DateTime? ExpiresAtUtc);

public sealed record BlockRuleOperationResult(bool Changed, BlockRuleInfo Rule);

/// <summary>Outcome of the idempotent built-in rule seeding at startup.</summary>
public sealed record BlockRuleSeedResult(int SeedVersion, int Added, int Updated)
{
    public bool Changed => Added > 0 || Updated > 0;
}
