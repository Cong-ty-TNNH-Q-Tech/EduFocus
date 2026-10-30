using EduFocus.Domain.Common;
using EduFocus.Domain.Enums;
using EduFocus.Domain.Privacy;
using EduFocus.Domain.ValueObjects;

namespace EduFocus.Domain.Entities;

/// <summary>
/// A best-effort observation of a blocked domain or application. Hosts-based
/// blocks cannot log HTTPS attempts, so counts are never treated as totals
/// (BR-10). Debouncing is handled by the use case, not here.
/// </summary>
public sealed class BlockedAttempt
{
    public const int MaxTargetValueLength = 512;
    public const int MaxProcessNameLength = 128;
    public const int MaxWindowTitleLength = 512;

    private BlockedAttempt(
        BlockedAttemptId id,
        StudySessionId? sessionId,
        DateTime occurredAtUtc,
        BlockedAttemptTargetType targetType,
        string targetValue,
        DomainName? domain,
        string? url,
        string? processName,
        string? windowTitle,
        BlockedAttemptObservedVia observedVia,
        DateTime createdAtUtc)
    {
        Id = id;
        SessionId = sessionId;
        OccurredAtUtc = occurredAtUtc;
        TargetType = targetType;
        TargetValue = targetValue;
        Domain = domain;
        Url = url;
        ProcessName = processName;
        WindowTitle = windowTitle;
        ObservedVia = observedVia;
        CreatedAtUtc = createdAtUtc;
    }

    public BlockedAttemptId Id { get; }

    public StudySessionId? SessionId { get; }

    public DateTime OccurredAtUtc { get; }

    public BlockedAttemptTargetType TargetType { get; }

    public string TargetValue { get; }

    public DomainName? Domain { get; }

    public string? Url { get; }

    public string? ProcessName { get; }

    public string? WindowTitle { get; }

    public BlockedAttemptObservedVia ObservedVia { get; }

    public DateTime CreatedAtUtc { get; }

    public static BlockedAttempt Record(
        DateTime occurredAtUtc,
        BlockedAttemptTargetType targetType,
        string targetValue,
        BlockedAttemptObservedVia observedVia,
        StudySessionId? sessionId = null,
        string? url = null,
        string? processName = null,
        string? windowTitle = null,
        DateTime? createdAtUtc = null)
    {
        Guard.Utc(occurredAtUtc, nameof(occurredAtUtc));
        Guard.Defined(targetType, nameof(targetType));
        Guard.Defined(observedVia, nameof(observedVia));
        Guard.NotNullOrWhiteSpace(targetValue, nameof(targetValue));
        Guard.MaxLength(targetValue, MaxTargetValueLength, nameof(targetValue));

        if (sessionId is { } session)
            Guard.NotDefault(session, nameof(sessionId));

        EnsureObservationMatches(targetType, observedVia);

        var created = createdAtUtc ?? occurredAtUtc;
        Guard.Utc(created, nameof(createdAtUtc));

        if (created < occurredAtUtc)
            throw new ArgumentException("Creation time must not precede the occurrence.", nameof(createdAtUtc));

        var sanitizedUrl = SanitizeOptionalUrl(url, nameof(url));
        DomainName? domain = null;
        string normalizedTarget;

        switch (targetType)
        {
            case BlockedAttemptTargetType.Domain:
                domain = DomainName.Parse(targetValue);
                normalizedTarget = domain.Value;
                break;

            case BlockedAttemptTargetType.Url:
                var sanitizedTarget = UrlSanitizer.Sanitize(targetValue)
                    ?? throw new ArgumentException("Target must be an absolute http/https URL.", nameof(targetValue));
                if (sanitizedTarget.Length > MaxTargetValueLength)
                    throw new ArgumentException("Target URL is too long.", nameof(targetValue));

                normalizedTarget = sanitizedTarget;
                sanitizedUrl ??= sanitizedTarget;
                domain = UrlSanitizer.DeriveDomain(sanitizedTarget);
                break;

            case BlockedAttemptTargetType.App:
                normalizedTarget = NormalizeProcessName(targetValue);
                if (sanitizedUrl is not null)
                    throw new ArgumentException("Application blocks do not carry a URL.", nameof(url));
                processName = TextSanitizer.Clean(processName, MaxProcessNameLength) ?? normalizedTarget;
                break;

            default:
                throw new ArgumentOutOfRangeException(nameof(targetType), targetType, "Unknown target type.");
        }

        return new BlockedAttempt(
            BlockedAttemptId.New(),
            sessionId,
            occurredAtUtc,
            targetType,
            normalizedTarget,
            domain,
            sanitizedUrl,
            TextSanitizer.Clean(processName, MaxProcessNameLength),
            TextSanitizer.Clean(windowTitle, MaxWindowTitleLength),
            observedVia,
            created);
    }

    private static void EnsureObservationMatches(
        BlockedAttemptTargetType targetType,
        BlockedAttemptObservedVia observedVia)
    {
        var valid = observedVia switch
        {
            BlockedAttemptObservedVia.BlockPage => targetType == BlockedAttemptTargetType.Domain,
            BlockedAttemptObservedVia.ProcessWatcher => targetType == BlockedAttemptTargetType.App,
            BlockedAttemptObservedVia.Extension =>
                targetType is BlockedAttemptTargetType.Domain or BlockedAttemptTargetType.Url,
            _ => false
        };

        if (!valid)
        {
            throw new ArgumentException(
                "Observed-via channel does not match the blocked target type.",
                nameof(observedVia));
        }
    }

    private static string? SanitizeOptionalUrl(string? url, string parameterName)
    {
        if (string.IsNullOrWhiteSpace(url))
            return null;

        return UrlSanitizer.Sanitize(url)
            ?? throw new ArgumentException("URL must be an absolute http/https URL.", parameterName);
    }

    private static string NormalizeProcessName(string value)
    {
        var name = value.Trim();

        var separator = name.LastIndexOfAny(['\\', '/']);
        if (separator >= 0)
            name = name[(separator + 1)..];

        if (name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
            name = name[..^4];

        name = name.Trim().ToLowerInvariant();
        if (name.Length is < 1 or > MaxProcessNameLength)
            throw new ArgumentException("Value must be a process name.", nameof(value));

        return name;
    }
}
