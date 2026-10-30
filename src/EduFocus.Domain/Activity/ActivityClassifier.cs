using EduFocus.Domain.Common;
using EduFocus.Domain.Enums;
using EduFocus.Domain.Protection;
using EduFocus.Domain.ValueObjects;

namespace EduFocus.Domain.Activity;

/// <summary>
/// Maps the foreground context to an activity category using the same rule
/// snapshot as the protection engine (UC07). Online classes are Study; an
/// explicit block wins over an allow for the same target, while an allow on
/// either the domain or the app yields Study. Missing context is Unknown and
/// an unmatched context is Neutral.
/// </summary>
public static class ActivityClassifier
{
    public static ActivityCategory Classify(
        BlockRuleSet rules,
        DomainName? domain,
        string? processName,
        bool isOnlineClass,
        DateTime nowUtc)
    {
        ArgumentNullException.ThrowIfNull(rules);
        Guard.Utc(nowUtc, nameof(nowUtc));

        if (isOnlineClass)
            return ActivityCategory.Study;

        var domainDecision = domain is null
            ? RuleDecision.None
            : rules.EvaluateDomain(domain, nowUtc);
        var appDecision = rules.EvaluateApp(processName, nowUtc);

        if (domainDecision == RuleDecision.Block || appDecision == RuleDecision.Block)
            return ActivityCategory.Distraction;

        if (domainDecision == RuleDecision.Allow || appDecision == RuleDecision.Allow)
            return ActivityCategory.Study;

        return domain is null && string.IsNullOrWhiteSpace(processName)
            ? ActivityCategory.Unknown
            : ActivityCategory.Neutral;
    }
}
