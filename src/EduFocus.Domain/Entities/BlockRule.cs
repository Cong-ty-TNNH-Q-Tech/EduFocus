using EduFocus.Domain.Common;
using EduFocus.Domain.Enums;
using EduFocus.Domain.Privacy;
using EduFocus.Domain.Protection;
using EduFocus.Domain.ValueObjects;

namespace EduFocus.Domain.Entities;

/// <summary>
/// A block or allow rule for a domain, application or URL. Domains match hosts
/// (exact, suffix or segment contains) and URLs match sanitized absolute URLs
/// with path rules enforced by the browser extension; application rules match
/// process names exactly (BR-09, BR-25).
/// </summary>
public sealed class BlockRule
{
    public const int MaxValueLength = 512;
    public const int MaxNoteLength = 512;

    private BlockRule(
        BlockRuleId id,
        RuleType ruleType,
        RuleMatchType matchType,
        string value,
        string normalizedValue,
        BlockRuleCategory category,
        BlockRuleEffect effect,
        bool isEnabled,
        bool isBuiltIn,
        EnforcementStatus enforcementStatus,
        DateTime? expiresAtUtc,
        string? note,
        DateTime createdAtUtc)
    {
        Id = id;
        RuleType = ruleType;
        MatchType = matchType;
        Value = value;
        NormalizedValue = normalizedValue;
        Category = category;
        Effect = effect;
        IsEnabled = isEnabled;
        IsBuiltIn = isBuiltIn;
        EnforcementStatus = enforcementStatus;
        ExpiresAtUtc = expiresAtUtc;
        Note = note;
        CreatedAtUtc = createdAtUtc;
        UpdatedAtUtc = createdAtUtc;
    }

    public BlockRuleId Id { get; }

    public RuleType RuleType { get; }

    public RuleMatchType MatchType { get; }

    public string Value { get; }

    public string NormalizedValue { get; }

    public BlockRuleCategory Category { get; private set; }

    public BlockRuleEffect Effect { get; }

    public bool IsEnabled { get; private set; }

    public bool IsBuiltIn { get; }

    public EnforcementStatus EnforcementStatus { get; }

    public DateTime? ExpiresAtUtc { get; private set; }

    public string? Note { get; private set; }

    public DateTime CreatedAtUtc { get; }

    public DateTime UpdatedAtUtc { get; private set; }

    public static BlockRule Create(
        RuleType ruleType,
        RuleMatchType matchType,
        string value,
        BlockRuleCategory category,
        BlockRuleEffect effect,
        DateTime nowUtc,
        bool isEnabled = true,
        bool isBuiltIn = false,
        DateTime? expiresAtUtc = null,
        string? note = null)
    {
        Guard.Utc(nowUtc, nameof(nowUtc));
        Guard.Utc(expiresAtUtc, nameof(expiresAtUtc));
        Guard.Defined(ruleType, nameof(ruleType));
        Guard.Defined(matchType, nameof(matchType));
        Guard.Defined(category, nameof(category));
        Guard.Defined(effect, nameof(effect));
        Guard.NotNullOrWhiteSpace(value, nameof(value));
        Guard.MaxLength(value, MaxValueLength, nameof(value));

        if (expiresAtUtc is not null && expiresAtUtc <= nowUtc)
            throw new ArgumentException("Expiry must be in the future.", nameof(expiresAtUtc));

        var trimmed = value.Trim();
        var normalized = Normalize(ruleType, trimmed);

        if (effect == BlockRuleEffect.Block && ResolveProtectedDomain(ruleType, normalized) is { } protectedDomain
            && ProtectedDomains.IsProtected(protectedDomain))
        {
            throw new ArgumentException(
                "Essential study and sign-in domains cannot be blocked.",
                nameof(value));
        }

        return new BlockRule(
            BlockRuleId.New(),
            ruleType,
            matchType,
            trimmed,
            normalized,
            category,
            effect,
            isEnabled,
            isBuiltIn,
            ResolveEnforcement(ruleType, matchType),
            expiresAtUtc,
            TextSanitizer.Clean(note, MaxNoteLength),
            nowUtc);
    }

    public bool IsEffective(DateTime nowUtc)
    {
        Guard.Utc(nowUtc, nameof(nowUtc));

        return IsEnabled
            && EnforcementStatus == EnforcementStatus.Enforced
            && (ExpiresAtUtc is null || ExpiresAtUtc > nowUtc);
    }

    /// <summary>Domain matching. The rule must be of type <see cref="RuleType.Domain"/>.</summary>
    public bool Matches(DomainName domain)
    {
        ArgumentNullException.ThrowIfNull(domain);

        if (RuleType != RuleType.Domain)
            return false;

        return MatchType switch
        {
            RuleMatchType.Exact => domain.Value.Equals(NormalizedValue, StringComparison.Ordinal),
            RuleMatchType.Suffix => domain.IsSameOrSubdomainOf(DomainName.Parse(NormalizedValue)),
            RuleMatchType.Contains => ContainsDomainSegment(domain.Value, NormalizedValue),
            _ => false
        };
    }

    /// <summary>Process matching for <see cref="RuleType.App"/>; paths and .exe are normalized.</summary>
    public bool MatchesProcess(string? processName)
    {
        if (RuleType != RuleType.App || MatchType != RuleMatchType.Exact)
            return false;

        return TryNormalizeProcessName(processName, out var normalized)
            && normalized.Equals(NormalizedValue, StringComparison.Ordinal);
    }

    /// <summary>
    /// URL matching for the browser extension. Query strings and fragments are
    /// stripped first (BR-13); a domain rule matches the URL host, a URL rule
    /// compares the sanitized URL. Paths are compared with a virtual trailing
    /// slash so <c>/shorts</c> and <c>/shorts/</c> behave the same.
    /// </summary>
    public bool MatchesUrl(string? url)
    {
        var sanitized = UrlSanitizer.Sanitize(url);
        if (sanitized is null)
            return false;

        if (RuleType == RuleType.Domain)
        {
            var domain = UrlSanitizer.DeriveDomain(sanitized);
            return domain is not null && Matches(domain);
        }

        if (RuleType != RuleType.Url)
            return false;

        var candidate = EnsureTrailingSlash(sanitized);

        return MatchType switch
        {
            RuleMatchType.Exact => candidate.Equals(NormalizedValue, StringComparison.OrdinalIgnoreCase),
            RuleMatchType.Suffix => candidate.EndsWith(NormalizedValue, StringComparison.OrdinalIgnoreCase),
            RuleMatchType.Contains => candidate.Contains(NormalizedValue, StringComparison.OrdinalIgnoreCase),
            _ => false
        };
    }

    public void SetEnabled(bool isEnabled, DateTime nowUtc)
    {
        IsEnabled = isEnabled;
        Touch(nowUtc);
    }

    public void SetExpiry(DateTime? expiresAtUtc, DateTime nowUtc)
    {
        Guard.Utc(nowUtc, nameof(nowUtc));
        Guard.Utc(expiresAtUtc, nameof(expiresAtUtc));

        if (expiresAtUtc is not null && expiresAtUtc <= nowUtc)
            throw new ArgumentException("Expiry must be in the future.", nameof(expiresAtUtc));

        ExpiresAtUtc = expiresAtUtc;
        Touch(nowUtc);
    }

    public void SetCategory(BlockRuleCategory category, DateTime nowUtc)
    {
        Guard.Defined(category, nameof(category));
        Category = category;
        Touch(nowUtc);
    }

    public void SetNote(string? note, DateTime nowUtc)
    {
        Guard.MaxLength(note, MaxNoteLength, nameof(note));
        Note = TextSanitizer.Clean(note, MaxNoteLength);
        Touch(nowUtc);
    }

    private static EnforcementStatus ResolveEnforcement(RuleType ruleType, RuleMatchType matchType) =>
        ruleType switch
        {
            RuleType.Domain or RuleType.Url => EnforcementStatus.Enforced,
            RuleType.App when matchType == RuleMatchType.Exact => EnforcementStatus.Enforced,
            _ => EnforcementStatus.NotEnforced
        };

    private static DomainName? ResolveProtectedDomain(RuleType ruleType, string normalizedValue) =>
        ruleType switch
        {
            RuleType.Domain => DomainName.Parse(normalizedValue),
            RuleType.Url => UrlSanitizer.DeriveDomain(normalizedValue),
            _ => null
        };

    private static string Normalize(RuleType ruleType, string value)
    {
        switch (ruleType)
        {
            case RuleType.Domain:
                return DomainName.Parse(value).Value;

            case RuleType.Url:
                var sanitized = UrlSanitizer.Sanitize(value)
                    ?? throw new ArgumentException("Value must be an absolute http/https URL.", nameof(value));
                return EnsureTrailingSlash(sanitized);

            case RuleType.App:
                if (!TryNormalizeProcessName(value, out var processName))
                    throw new ArgumentException("Value must be a process name.", nameof(value));
                return processName;

            default:
                throw new ArgumentOutOfRangeException(nameof(ruleType), ruleType, "Unknown rule type.");
        }
    }

    private static string EnsureTrailingSlash(string url) =>
        url.EndsWith('/') ? url : url + "/";

    private static bool TryNormalizeProcessName(string? value, out string normalized)
    {
        normalized = string.Empty;
        if (string.IsNullOrWhiteSpace(value))
            return false;

        var name = value.Trim();

        var separator = name.LastIndexOfAny(['\\', '/']);
        if (separator >= 0)
            name = name[(separator + 1)..];

        if (name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
            name = name[..^4];

        name = name.Trim().ToLowerInvariant();
        if (name.Length is < 1 or > 128)
            return false;

        normalized = name;
        return true;
    }

    private static bool ContainsDomainSegment(string domain, string segment) =>
        domain.Equals(segment, StringComparison.Ordinal)
        || domain.EndsWith("." + segment, StringComparison.Ordinal)
        || domain.Contains("." + segment + ".", StringComparison.Ordinal);

    private void Touch(DateTime nowUtc)
    {
        Guard.Utc(nowUtc, nameof(nowUtc));

        if (nowUtc < UpdatedAtUtc)
            throw new ArgumentException("Timestamp must not move backwards.", nameof(nowUtc));

        UpdatedAtUtc = nowUtc;
    }
}
