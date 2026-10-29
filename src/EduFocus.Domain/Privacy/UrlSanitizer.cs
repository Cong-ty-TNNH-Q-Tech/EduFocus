using EduFocus.Domain.ValueObjects;

namespace EduFocus.Domain.Privacy;

/// <summary>
/// Strips query strings, fragments and credentials before a URL is persisted
/// (BR-13, database schema §9). Only http/https URLs are accepted.
/// </summary>
public static class UrlSanitizer
{
    public const int MaxLength = 2048;

    public static string? Sanitize(string? url)
    {
        if (string.IsNullOrWhiteSpace(url))
            return null;

        if (!Uri.TryCreate(url.Trim(), UriKind.Absolute, out var uri))
            return null;

        if (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps)
            return null;

        if (string.IsNullOrEmpty(uri.Host))
            return null;

        var builder = new UriBuilder(uri)
        {
            UserName = string.Empty,
            Password = string.Empty,
            Query = string.Empty,
            Fragment = string.Empty
        };

        var sanitized = builder.Uri.GetComponents(
            UriComponents.SchemeAndServer | UriComponents.Path,
            UriFormat.UriEscaped);

        return sanitized.Length > MaxLength ? null : sanitized;
    }

    public static DomainName? DeriveDomain(string? url)
    {
        var sanitized = Sanitize(url);
        if (sanitized is null)
            return null;

        return Uri.TryCreate(sanitized, UriKind.Absolute, out var uri)
            && DomainName.TryParse(uri.Host, out var domain)
                ? domain
                : null;
    }
}
