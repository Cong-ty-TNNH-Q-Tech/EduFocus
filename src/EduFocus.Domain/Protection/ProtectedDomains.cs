using System.Collections.Frozen;
using EduFocus.Domain.ValueObjects;

namespace EduFocus.Domain.Protection;

/// <summary>
/// Domains that can never be blocked because they carry shared services,
/// online classes or sign-in flows (BR-09, BR-24).
/// <para>
/// Shared apex domains (<c>google.com</c>, <c>microsoft.com</c>) are protected
/// only as exact hosts, so specific services such as <c>gemini.google.com</c>
/// or <c>copilot.microsoft.com</c> remain blockable. Listed service domains are
/// protected together with their subdomains.
/// </para>
/// </summary>
public static class ProtectedDomains
{
    private static readonly FrozenSet<string> SharedApex = new[]
    {
        "google.com",
        "microsoft.com"
    }.ToFrozenSet(StringComparer.Ordinal);

    private static readonly FrozenSet<string> Services = new[]
    {
        "meet.google.com",
        "teams.microsoft.com",
        "classroom.google.com",
        "accounts.google.com",
        "login.microsoftonline.com",
        "login.live.com",
        "zoom.us"
    }.ToFrozenSet(StringComparer.Ordinal);

    private static readonly FrozenSet<string> AllProtected =
        SharedApex.Concat(Services).ToFrozenSet(StringComparer.Ordinal);

    public static IReadOnlyCollection<string> All => AllProtected;

    public static bool IsProtected(DomainName domain)
    {
        ArgumentNullException.ThrowIfNull(domain);

        if (SharedApex.Contains(domain.Value))
            return true;

        foreach (var service in Services)
        {
            if (domain.Value.Equals(service, StringComparison.Ordinal)
                || domain.Value.EndsWith("." + service, StringComparison.Ordinal))
            {
                return true;
            }
        }

        return false;
    }
}
