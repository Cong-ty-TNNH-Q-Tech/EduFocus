using EduFocus.Domain.ValueObjects;

namespace EduFocus.Domain.Activity;

/// <summary>
/// Detects online-class contexts from the foreground domain, process or window
/// title (UC07, architecture §7.3). Detection is a best-effort overlay; it does
/// not change whether a domain is blocked. Generic chat titles are ignored so
/// Discord, Slack, Messenger or Zalo are never mistaken for a class.
/// </summary>
public static class OnlineClassDetector
{
    private static readonly string[] ClassDomains =
    [
        "meet.google.com",
        "teams.microsoft.com",
        "zoom.us"
    ];

    private static readonly string[] ClassProcesses =
    [
        "ms-teams",
        "teams",
        "zoom"
    ];

    public static bool IsOnlineClass(
        DomainName? domain,
        string? processName,
        string? windowTitle)
    {
        if (domain is not null)
        {
            foreach (var classDomain in ClassDomains)
            {
                if (domain.IsSameOrSubdomainOf(DomainName.Parse(classDomain)))
                    return true;
            }
        }

        var process = NormalizeProcess(processName);
        if (process.Length > 0 && ClassProcesses.Contains(process, StringComparer.Ordinal))
            return true;

        return IsMeetWindowTitle(windowTitle);
    }

    private static bool IsMeetWindowTitle(string? windowTitle)
    {
        if (string.IsNullOrWhiteSpace(windowTitle))
            return false;

        var title = windowTitle.Trim();

        if (title.Contains("Google Meet", StringComparison.OrdinalIgnoreCase))
            return true;

        if (!title.StartsWith("Meet", StringComparison.OrdinalIgnoreCase))
            return false;

        if (title.Length == 4)
            return true;

        // Require a separator after "Meet" so titles such as "Meeting notes" or
        // "Meet the team" are not treated as a class.
        var rest = title[4..].TrimStart();
        return rest.Length == 0 || rest[0] is '-' or '–' or '—' or '|' or ':';
    }

    private static string NormalizeProcess(string? processName)
    {
        if (string.IsNullOrWhiteSpace(processName))
            return string.Empty;

        var name = processName.Trim().Trim('"');
        var separator = name.LastIndexOfAny(['\\', '/']);
        if (separator >= 0)
            name = name[(separator + 1)..];

        if (name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
            name = name[..^4];

        return name.ToLowerInvariant();
    }
}
