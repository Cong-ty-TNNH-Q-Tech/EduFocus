using System.Text;

namespace EduFocus.Cloud.Contracts.Pairing;

public sealed record PairingUriData(string CloudBaseUrl, Guid SessionId, string Code);

public static class PairingUri
{
    public const string Prefix = "edufocus://pair?v=1";

    public static string Build(string cloudBaseUrl, Guid sessionId, string code)
    {
        var encodedUrl = Base64UrlEncode(Encoding.UTF8.GetBytes(cloudBaseUrl.TrimEnd('/')));
        return $"{Prefix}&u={encodedUrl}&s={sessionId:D}&c={code}";
    }

    public static bool TryParse(string? value, out PairingUriData data)
    {
        data = null!;
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        var trimmed = value.Trim();
        if (!trimmed.StartsWith(Prefix + "&", StringComparison.Ordinal))
        {
            return false;
        }

        string? encodedUrl = null;
        string? session = null;
        string? code = null;
        foreach (var pair in trimmed[(Prefix.Length + 1)..].Split('&', StringSplitOptions.RemoveEmptyEntries))
        {
            var separator = pair.IndexOf('=');
            if (separator <= 0)
            {
                return false;
            }

            var key = pair[..separator];
            var pairValue = Uri.UnescapeDataString(pair[(separator + 1)..]);
            switch (key)
            {
                case "u":
                    encodedUrl = pairValue;
                    break;
                case "s":
                    session = pairValue;
                    break;
                case "c":
                    code = pairValue;
                    break;
            }
        }

        if (string.IsNullOrWhiteSpace(encodedUrl)
            || encodedUrl.Length > 2048
            || !Guid.TryParse(session, out var sessionId)
            || string.IsNullOrWhiteSpace(code))
        {
            return false;
        }

        string cloudBaseUrl;
        try
        {
            cloudBaseUrl = Encoding.UTF8.GetString(Base64UrlDecode(encodedUrl)).TrimEnd('/');
        }
        catch (FormatException)
        {
            return false;
        }

        if (!Uri.TryCreate(cloudBaseUrl, UriKind.Absolute, out var uri)
            || (uri.Scheme != Uri.UriSchemeHttps && uri.Scheme != Uri.UriSchemeHttp))
        {
            return false;
        }

        data = new PairingUriData(cloudBaseUrl, sessionId, code.ToUpperInvariant());
        return true;
    }

    private static string Base64UrlEncode(byte[] bytes) =>
        Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');

    private static byte[] Base64UrlDecode(string value)
    {
        var payload = value.Replace('-', '+').Replace('_', '/');
        payload = payload.PadRight(payload.Length + (4 - payload.Length % 4) % 4, '=');
        return Convert.FromBase64String(payload);
    }
}
