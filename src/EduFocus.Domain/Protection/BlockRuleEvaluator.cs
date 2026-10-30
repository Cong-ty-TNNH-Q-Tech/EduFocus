using EduFocus.Domain.Common;
using EduFocus.Domain.Entities;
using EduFocus.Domain.Enums;
using EduFocus.Domain.Privacy;
using EduFocus.Domain.ValueObjects;

namespace EduFocus.Domain.Protection;

/// <summary>
/// Evaluates effective rules. An allow rule always wins over a block rule (BR-08);
/// expired, disabled and not-enforced rules are ignored.
/// </summary>
public static class BlockRuleEvaluator
{
    public static RuleDecision EvaluateDomain(
        IEnumerable<BlockRule> rules,
        DomainName domain,
        DateTime nowUtc)
    {
        ArgumentNullException.ThrowIfNull(rules);
        ArgumentNullException.ThrowIfNull(domain);
        Guard.Utc(nowUtc, nameof(nowUtc));

        var allowed = false;
        var blocked = false;

        foreach (var rule in rules)
        {
            if (rule.RuleType != RuleType.Domain
                || !rule.IsEffective(nowUtc)
                || !rule.Matches(domain))
            {
                continue;
            }

            if (rule.Effect == BlockRuleEffect.Allow)
                allowed = true;
            else
                blocked = true;
        }

        return allowed ? RuleDecision.Allow : blocked ? RuleDecision.Block : RuleDecision.None;
    }

    public static RuleDecision EvaluateApp(
        IEnumerable<BlockRule> rules,
        string? processName,
        DateTime nowUtc)
    {
        ArgumentNullException.ThrowIfNull(rules);
        Guard.Utc(nowUtc, nameof(nowUtc));

        var allowed = false;
        var blocked = false;

        foreach (var rule in rules)
        {
            if (rule.RuleType != RuleType.App
                || !rule.IsEffective(nowUtc)
                || !rule.MatchesProcess(processName))
            {
                continue;
            }

            if (rule.Effect == BlockRuleEffect.Allow)
                allowed = true;
            else
                blocked = true;
        }

        return allowed ? RuleDecision.Allow : blocked ? RuleDecision.Block : RuleDecision.None;
    }

    /// <summary>
    /// Evaluates a full URL for the browser extension. Domain and URL rules both
    /// participate; allow still wins. URLs that cannot be parsed yield
    /// <see cref="RuleDecision.None"/> so a malformed request never blocks.
    /// </summary>
    public static RuleDecision EvaluateUrl(
        IEnumerable<BlockRule> rules,
        string? url,
        DateTime nowUtc)
    {
        ArgumentNullException.ThrowIfNull(rules);
        Guard.Utc(nowUtc, nameof(nowUtc));

        if (UrlSanitizer.Sanitize(url) is null)
            return RuleDecision.None;

        var allowed = false;
        var blocked = false;

        foreach (var rule in rules)
        {
            if (!rule.IsEffective(nowUtc) || !rule.MatchesUrl(url))
                continue;

            if (rule.Effect == BlockRuleEffect.Allow)
                allowed = true;
            else
                blocked = true;
        }

        return allowed ? RuleDecision.Allow : blocked ? RuleDecision.Block : RuleDecision.None;
    }
}
