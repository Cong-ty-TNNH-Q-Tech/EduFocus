using System.Security.Cryptography;
using System.Text;
using EduFocus.Cloud.Data.Entities;

namespace EduFocus.Cloud.Api.Auth;

public static class DeviceTokenService
{
    public const string DesktopPrefix = "efd_";
    public const string MobilePrefix = "efm_";

    private const int TokenBytes = 32;

    public static (string Token, string Hash) Create(DeviceKind kind)
    {
        var prefix = kind == DeviceKind.Desktop ? DesktopPrefix : MobilePrefix;
        var token = prefix + Base64UrlEncode(RandomNumberGenerator.GetBytes(TokenBytes));
        return (token, Hash(token));
    }

    public static bool TryHash(string? token, out string hash)
    {
        hash = string.Empty;
        if (string.IsNullOrWhiteSpace(token))
        {
            return false;
        }

        var hasPrefix = token.StartsWith(DesktopPrefix, StringComparison.Ordinal)
            || token.StartsWith(MobilePrefix, StringComparison.Ordinal);
        if (!hasPrefix || token.Length != 4 + 43)
        {
            return false;
        }

        try
        {
            var payload = token[4..].Replace('-', '+').Replace('_', '/');
            payload = payload.PadRight(payload.Length + (4 - payload.Length % 4) % 4, '=');
            if (Convert.FromBase64String(payload).Length != TokenBytes)
            {
                return false;
            }
        }
        catch (FormatException)
        {
            return false;
        }

        hash = Hash(token);
        return true;
    }

    public static string Hash(string token) =>
        Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(token)));

    private static string Base64UrlEncode(byte[] bytes) =>
        Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');
}
