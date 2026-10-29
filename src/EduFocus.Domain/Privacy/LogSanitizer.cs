using System.Text.RegularExpressions;

namespace EduFocus.Domain.Privacy;

/// <summary>
/// Removes secrets and URL detail before text reaches a log file
/// (system architecture §12). PINs, recovery codes, tokens and query strings
/// are never written; only the normalized domain of a URL survives.
/// </summary>
public static class LogSanitizer
{
    public const int MaxLength = 4096;
    private const string TruncatedSuffix = "...[truncated]";

    private static readonly Regex AuthorizationPattern = new(
        @"\bauthorization\b\s*[:=]\s*[^\r\n]+",
        RegexOptions.IgnoreCase | RegexOptions.NonBacktracking);

    private static readonly Regex BearerPattern = new(
        @"\bBearer\s+[A-Za-z0-9\-._~+/]+=*",
        RegexOptions.IgnoreCase | RegexOptions.NonBacktracking);

    private static readonly Regex SecretPattern = new(
        @"\b(?<key>[A-Za-z0-9_.-]*(?:pin|pwd|pass|password|secret|token|api[_-]?key|recovery(?:[ _-]?code)?))\b\s*[:=]\s*(?:""[^""]*""|'[^']*'|\S+)",
        RegexOptions.IgnoreCase | RegexOptions.NonBacktracking);

    private static readonly Regex SecretWithoutSeparatorPattern = new(
        @"\b(?<key>pin|recovery(?:[ _-]?code)?)\s+(?:(?:is|was)\s+)?(?<value>\d{4,12}|[A-Z2-9]{4}(?:-?[A-Z2-9]{4}){2})\b",
        RegexOptions.IgnoreCase | RegexOptions.NonBacktracking);

    private static readonly Regex UrlPattern = new(
        @"\bhttps?://[^\s""'<>]+",
        RegexOptions.IgnoreCase | RegexOptions.NonBacktracking);

    /// <summary>Redacts secrets and URLs, then collapses the text to one line.</summary>
    public static string Redact(string? text)
    {
        if (string.IsNullOrEmpty(text))
            return string.Empty;

        var redacted = ApplyRedaction(text)
            .Replace("\r\n", " ", StringComparison.Ordinal)
            .Replace('\r', ' ')
            .Replace('\n', ' ')
            .Replace('\t', ' ');

        return Truncate(redacted.Trim());
    }

    /// <summary>
    /// Same redaction as <see cref="Redact"/> but keeps line breaks so stack
    /// traces stay readable. Callers must only pass technical exception text.
    /// </summary>
    public static string RedactPreservingLines(string? text)
    {
        if (string.IsNullOrEmpty(text))
            return string.Empty;

        return Truncate(ApplyRedaction(text).Trim());
    }

    private static string ApplyRedaction(string text)
    {
        var result = AuthorizationPattern.Replace(text, match =>
        {
            var separatorIndex = match.Value.IndexOfAny([':', '=']);
            var key = match.Value[..separatorIndex].Trim();
            return $"{key}{match.Value[separatorIndex]}***";
        });

        result = BearerPattern.Replace(result, "Bearer ***");
        result = SecretPattern.Replace(result, match => $"{match.Groups["key"].Value}=***");
        result = SecretWithoutSeparatorPattern.Replace(
            result,
            match => $"{match.Groups["key"].Value}=***");
        result = UrlPattern.Replace(result, RedactUrl);
        return result;
    }

    private static string RedactUrl(Match match)
    {
        var value = match.Value;
        var trimmed = value.TrimEnd('.', ',', ';', ':', '!', '?', ')', ']', '}');
        var trailing = value[trimmed.Length..];

        var domain = UrlSanitizer.DeriveDomain(trimmed);
        return domain is null ? $"[url]{trailing}" : $"[url:{domain.Value}]{trailing}";
    }

    private static string Truncate(string text) =>
        text.Length <= MaxLength ? text : text[..MaxLength] + TruncatedSuffix;
}
