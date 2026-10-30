using EduFocus.Domain.Common;
using EduFocus.Domain.Enums;
using EduFocus.Domain.Privacy;
using EduFocus.Domain.ValueObjects;

namespace EduFocus.Domain.Entities;

/// <summary>
/// A periodic snapshot of foreground activity. Titles are truncated and URLs are
/// stripped of query strings, fragments and credentials before storage (BR-13).
/// </summary>
public sealed class ActivitySample
{
    public const int MaxAppNameLength = 128;
    public const int MaxProcessNameLength = 128;
    public const int MaxWindowTitleLength = 512;

    private ActivitySample(
        ActivitySampleId id,
        StudySessionId sessionId,
        DateTime sampledAtUtc,
        int durationSeconds,
        string? appName,
        string? processName,
        string? windowTitle,
        string? url,
        DomainName? domain,
        UrlSource urlSource,
        ActivityCategory category,
        bool isForeground,
        bool isOnlineClass,
        int idleSeconds,
        DateTime createdAtUtc)
    {
        Id = id;
        SessionId = sessionId;
        SampledAtUtc = sampledAtUtc;
        DurationSeconds = durationSeconds;
        AppName = appName;
        ProcessName = processName;
        WindowTitle = windowTitle;
        Url = url;
        Domain = domain;
        UrlSource = urlSource;
        Category = category;
        IsForeground = isForeground;
        IsOnlineClass = isOnlineClass;
        IdleSeconds = idleSeconds;
        CreatedAtUtc = createdAtUtc;
    }

    public ActivitySampleId Id { get; }

    public StudySessionId SessionId { get; }

    public DateTime SampledAtUtc { get; }

    public int DurationSeconds { get; }

    public string? AppName { get; }

    public string? ProcessName { get; }

    public string? WindowTitle { get; }

    public string? Url { get; }

    public DomainName? Domain { get; }

    public UrlSource UrlSource { get; }

    public ActivityCategory Category { get; }

    public bool IsForeground { get; }

    public bool IsOnlineClass { get; }

    public int IdleSeconds { get; }

    public DateTime CreatedAtUtc { get; }

    public static ActivitySample Capture(
        StudySessionId sessionId,
        DateTime sampledAtUtc,
        int durationSeconds,
        ActivityCategory category,
        bool isForeground,
        bool isOnlineClass,
        int idleSeconds = 0,
        string? appName = null,
        string? processName = null,
        string? windowTitle = null,
        string? url = null,
        UrlSource urlSource = UrlSource.None,
        DateTime? createdAtUtc = null)
    {
        Guard.NotDefault(sessionId, nameof(sessionId));
        Guard.Utc(sampledAtUtc, nameof(sampledAtUtc));
        Guard.NonNegative(durationSeconds, nameof(durationSeconds));
        Guard.InRange(idleSeconds, 0, durationSeconds, nameof(idleSeconds));
        Guard.Defined(category, nameof(category));
        Guard.Defined(urlSource, nameof(urlSource));

        var created = createdAtUtc ?? sampledAtUtc;
        Guard.Utc(created, nameof(createdAtUtc));

        if (created < sampledAtUtc)
            throw new ArgumentException("Creation time must not precede sampling time.", nameof(createdAtUtc));

        var sanitizedUrl = SanitizeUrl(url, urlSource, nameof(url));
        var domain = sanitizedUrl is null ? null : UrlSanitizer.DeriveDomain(sanitizedUrl);

        return new ActivitySample(
            ActivitySampleId.New(),
            sessionId,
            sampledAtUtc,
            durationSeconds,
            TextSanitizer.Clean(appName, MaxAppNameLength),
            TextSanitizer.Clean(processName, MaxProcessNameLength),
            TextSanitizer.Clean(windowTitle, MaxWindowTitleLength),
            sanitizedUrl,
            domain,
            urlSource,
            category,
            isForeground,
            isOnlineClass,
            idleSeconds,
            created);
    }

    private static string? SanitizeUrl(string? url, UrlSource urlSource, string parameterName)
    {
        var hasUrl = !string.IsNullOrWhiteSpace(url);
        if (!hasUrl)
        {
            if (urlSource != UrlSource.None)
                throw new ArgumentException("A URL source requires a URL.", parameterName);

            return null;
        }

        if (urlSource == UrlSource.None)
            throw new ArgumentException("A URL requires a URL source.", parameterName);

        return UrlSanitizer.Sanitize(url)
            ?? throw new ArgumentException("URL must be an absolute http/https URL.", parameterName);
    }
}
