using System.Net;
using System.Net.Sockets;

namespace EduFocus.UseCases.Cloud;

/// <summary>
/// Normalises the cloud relay base URL. HTTPS is mandatory for real use; a
/// plain HTTP endpoint is accepted only in development builds and only for
/// localhost, loopback or private LAN addresses (issue #86).
/// </summary>
public static class CloudUrlPolicy
{
    public static bool TryNormalize(
        string? value,
        bool allowInsecureDevelopmentEndpoints,
        out Uri? cloudBaseUrl,
        out string? error)
    {
        cloudBaseUrl = null;
        error = null;

        var trimmed = value?.Trim();
        if (string.IsNullOrWhiteSpace(trimmed))
        {
            error = "Nhập địa chỉ máy chủ cloud.";
            return false;
        }

        if (!Uri.TryCreate(trimmed, UriKind.Absolute, out var uri))
        {
            error = "Địa chỉ cloud không hợp lệ. Ví dụ: https://cloud.example.com";
            return false;
        }

        if (!string.IsNullOrEmpty(uri.UserInfo))
        {
            error = "Địa chỉ cloud không được chứa tên đăng nhập hoặc mật khẩu.";
            return false;
        }

        if (uri.Scheme != Uri.UriSchemeHttps && uri.Scheme != Uri.UriSchemeHttp)
        {
            error = "Địa chỉ cloud chỉ hỗ trợ http hoặc https.";
            return false;
        }

        if (uri.Scheme == Uri.UriSchemeHttp)
        {
            if (!allowInsecureDevelopmentEndpoints)
            {
                error = "Cloud bắt buộc dùng HTTPS. Địa chỉ http chỉ dùng được trong bản phát triển.";
                return false;
            }

            if (!IsLocalOrPrivateHost(uri.DnsSafeHost))
            {
                error = "Bản phát triển chỉ cho phép http với localhost hoặc địa chỉ IP mạng LAN.";
                return false;
            }
        }

        if (uri.AbsolutePath is not ("" or "/")
            || !string.IsNullOrEmpty(uri.Query)
            || !string.IsNullOrEmpty(uri.Fragment))
        {
            error = "Chỉ nhập địa chỉ gốc của cloud, không kèm đường dẫn, tham số hoặc neo.";
            return false;
        }

        cloudBaseUrl = new Uri(uri.GetLeftPart(UriPartial.Authority));
        return true;
    }

    private static bool IsLocalOrPrivateHost(string host)
    {
        if (host.Equals("localhost", StringComparison.OrdinalIgnoreCase))
            return true;

        if (!IPAddress.TryParse(host, out var address))
            return false;

        if (IPAddress.IsLoopback(address))
            return true;

        if (address.AddressFamily == AddressFamily.InterNetwork)
        {
            var bytes = address.GetAddressBytes();
            return bytes[0] == 10
                || (bytes[0] == 172 && bytes[1] is >= 16 and <= 31)
                || (bytes[0] == 192 && bytes[1] == 168);
        }

        return address.IsIPv6LinkLocal || address.IsIPv6UniqueLocal;
    }
}
