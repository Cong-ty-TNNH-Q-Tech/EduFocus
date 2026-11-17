using System.Reflection;

namespace EduFocus.WPF.Services;

/// <summary>
/// Build-time defaults for the optional cloud relay. The base URL comes from
/// assembly metadata (Debug points at the local dev API, Release stays empty so
/// self-hosters must type their own HTTPS endpoint); it never embeds secrets.
/// </summary>
public static class CloudRelayDefaults
{
    public const string BaseUrlMetadataKey = "CloudRelayBaseUrl";
    public const string FallbackDesktopName = "EduFocus Desktop";

    public static string GetBaseUrl()
    {
        var value = typeof(CloudRelayDefaults).Assembly
            .GetCustomAttributes<AssemblyMetadataAttribute>()
            .FirstOrDefault(attribute =>
                string.Equals(attribute.Key, BaseUrlMetadataKey, StringComparison.Ordinal))
            ?.Value;

        return value?.Trim() ?? string.Empty;
    }

    public static string GetDesktopName()
    {
        try
        {
            var name = Environment.MachineName?.Trim();
            if (!string.IsNullOrEmpty(name))
                return name.Length <= 128 ? name : name[..128];
        }
        catch (InvalidOperationException)
        {
            // MachineName needs permission; the pairing name is only a label.
        }

        return FallbackDesktopName;
    }
}
