using EduFocus.Cloud.Contracts.Pairing;

namespace EduFocus.Parent.Core.Pairing;

public sealed record PairingPayload(string CloudBaseUrl, Guid SessionId, string Code);

/// <summary>
/// Parses the QR/deep-link payload <c>edufocus://pair?v=1&amp;u=...&amp;s=...&amp;c=...</c>.
/// Pure and side-effect free so it can be unit tested without camera or UI.
/// </summary>
public static class PairingPayloadParser
{
    public const string UriPrefix = "edufocus://pair";

    public static bool TryParse(
        string? value,
        bool allowInsecureEndpoints,
        out PairingPayload? payload,
        out string? error)
    {
        payload = null;
        error = null;

        var trimmed = value?.Trim();
        if (string.IsNullOrWhiteSpace(trimmed)
            || !trimmed.StartsWith(UriPrefix, StringComparison.OrdinalIgnoreCase))
        {
            error = "Mã QR không phải mã ghép nối EduFocus.";
            return false;
        }

        if (!PairingUri.TryParse(trimmed, out var uriData))
        {
            error = "Mã QR sai định dạng hoặc không đúng phiên bản hỗ trợ.";
            return false;
        }

        if (!CloudServerUrl.TryNormalize(
                uriData.CloudBaseUrl,
                allowInsecureEndpoints,
                out var cloudBaseUrl,
                out var urlError))
        {
            error = urlError ?? "Địa chỉ cloud trong mã QR không hợp lệ.";
            return false;
        }

        if (!PairingCodeInput.TryNormalize(uriData.Code, out var code))
        {
            error = "Mã ghép nối trong QR không hợp lệ.";
            return false;
        }

        payload = new PairingPayload(cloudBaseUrl!.GetLeftPart(UriPartial.Authority), uriData.SessionId, code);
        return true;
    }
}
