namespace EduFocus.Cloud.Api.Push;

/// <summary>
/// Validates UnifiedPush subscriptions before they are stored or used. Web Push
/// endpoints must be HTTPS and, when push is enabled, match the configured
/// host allowlist to prevent the API from becoming an SSRF proxy.
/// </summary>
public static class PushEndpointValidator
{
    public const int MaxEndpointLength = 1000;
    public const int MaxP256dhLength = 128;
    public const int MaxAuthLength = 64;

    public static bool TryValidateRegistration(
        string? endpoint,
        string? p256dh,
        string? auth,
        IReadOnlyList<string> allowedHosts,
        bool enforceAllowedHosts,
        out string normalizedEndpoint,
        out string normalizedP256dh,
        out string normalizedAuth)
    {
        normalizedEndpoint = endpoint?.Trim() ?? string.Empty;
        normalizedP256dh = p256dh?.Trim() ?? string.Empty;
        normalizedAuth = auth?.Trim() ?? string.Empty;

        if (string.IsNullOrEmpty(normalizedEndpoint)
            || normalizedEndpoint.Length > MaxEndpointLength
            || normalizedEndpoint.Any(char.IsControl)
            || !Uri.TryCreate(normalizedEndpoint, UriKind.Absolute, out var uri)
            || uri.Scheme != Uri.UriSchemeHttps
            || string.IsNullOrWhiteSpace(uri.Host)
            || !string.IsNullOrEmpty(uri.UserInfo))
        {
            return false;
        }

        if (enforceAllowedHosts && !IsEndpointAllowed(uri, allowedHosts))
            return false;

        return IsValidP256dh(normalizedP256dh) && IsValidAuth(normalizedAuth);
    }

    public static bool IsEndpointAllowed(string endpoint, IReadOnlyList<string> allowedHosts) =>
        Uri.TryCreate(endpoint, UriKind.Absolute, out var uri)
        && uri.Scheme == Uri.UriSchemeHttps
        && string.IsNullOrEmpty(uri.UserInfo)
        && IsEndpointAllowed(uri, allowedHosts);

    public static bool IsValidAllowedHost(string? value) =>
        TryParseAllowedHost(value, out _, out _);

    public static bool IsValidVapidPublicKey(string? value) =>
        TryDecodeBase64Url(value, 65, firstByte: 0x04);

    public static bool IsValidVapidPrivateKey(string? value) =>
        TryDecodeBase64Url(value, 32, firstByte: null);

    private static bool IsEndpointAllowed(Uri uri, IReadOnlyList<string> allowedHosts)
    {
        foreach (var allowed in allowedHosts)
        {
            if (!TryParseAllowedHost(allowed, out var host, out var port))
                continue;

            if (!string.Equals(uri.Host, host, StringComparison.OrdinalIgnoreCase))
                continue;

            if (port is null || uri.Port == port)
                return true;
        }

        return false;
    }

    private static bool TryParseAllowedHost(string? value, out string host, out int? port)
    {
        host = string.Empty;
        port = null;

        if (string.IsNullOrWhiteSpace(value))
            return false;

        var trimmed = value.Trim();
        if (trimmed.Any(char.IsControl)
            || trimmed.Contains("://", StringComparison.Ordinal)
            || trimmed.Contains('/', StringComparison.Ordinal)
            || trimmed.Contains('?', StringComparison.Ordinal)
            || trimmed.Contains('#', StringComparison.Ordinal))
        {
            return false;
        }

        if (!Uri.TryCreate("https://" + trimmed, UriKind.Absolute, out var uri)
            || uri.AbsolutePath != "/"
            || uri.Query.Length > 0
            || uri.Fragment.Length > 0
            || string.IsNullOrWhiteSpace(uri.Host))
        {
            return false;
        }

        host = uri.Host;
        port = uri.IsDefaultPort ? null : uri.Port;
        return true;
    }

    private static bool IsValidP256dh(string value) =>
        TryDecodeBase64Url(value, 65, firstByte: 0x04);

    private static bool IsValidAuth(string value) =>
        TryDecodeBase64Url(value, 16, firstByte: null);

    private static bool TryDecodeBase64Url(string? value, int expectedLength, byte? firstByte)
    {
        if (string.IsNullOrEmpty(value)
            || value.Length > 512
            || value.Any(ch => !char.IsAsciiLetterOrDigit(ch) && ch is not '-' and not '_'))
        {
            return false;
        }

        var padded = value.Replace('-', '+').Replace('_', '/');
        padded = (padded.Length % 4) switch
        {
            2 => padded + "==",
            3 => padded + "=",
            _ => padded,
        };

        try
        {
            var bytes = Convert.FromBase64String(padded);
            return bytes.Length == expectedLength
                && (firstByte is null || bytes[0] == firstByte.Value);
        }
        catch (FormatException)
        {
            return false;
        }
    }
}
