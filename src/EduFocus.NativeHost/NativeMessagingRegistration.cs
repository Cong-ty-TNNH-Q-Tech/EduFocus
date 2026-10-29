using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Win32;

namespace EduFocus.NativeHost;

public sealed record NativeMessagingManifest(
    [property: JsonPropertyName("name")] string Name,
    [property: JsonPropertyName("description")] string Description,
    [property: JsonPropertyName("path")] string Path,
    [property: JsonPropertyName("type")] string Type,
    [property: JsonPropertyName("allowed_origins")] string[] AllowedOrigins);

/// <summary>
/// Writes the Chrome/Edge native messaging manifest and registry keys. The MSI
/// runs <c>--register --scope machine</c> as SYSTEM after installing the host;
/// the host itself never needs the registry at run time.
/// </summary>
public static class NativeMessagingRegistration
{
    public const string HostName = "com.edufocus.native_host";
    public const string ExtensionId = "lcanbfnejggilcaeinmahlcfimecmjmp";
    public const string ManifestFileName = HostName + ".json";

    private const string Description = "EduFocus browser extension bridge";
    private const string ProtocolType = "stdio";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true
    };

    private static readonly string[] BrowserKeySuffixes =
    [
        $@"Software\Google\Chrome\NativeMessagingHosts\{HostName}",
        $@"Software\Microsoft\Edge\NativeMessagingHosts\{HostName}"
    ];

    public static IReadOnlyList<string> RegistryKeySuffixes => BrowserKeySuffixes;

    public static string GetManifestPath(string manifestDirectory) =>
        System.IO.Path.Combine(manifestDirectory, ManifestFileName);

    public static string BuildManifestJson(string hostPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(hostPath);

        var manifest = new NativeMessagingManifest(
            HostName,
            Description,
            System.IO.Path.GetFullPath(hostPath),
            ProtocolType,
            [$"chrome-extension://{ExtensionId}/"]);

        return JsonSerializer.Serialize(manifest, JsonOptions);
    }

    public static string WriteManifest(string hostPath, string manifestDirectory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(manifestDirectory);

        Directory.CreateDirectory(manifestDirectory);
        var manifestPath = GetManifestPath(manifestDirectory);
        File.WriteAllText(
            manifestPath,
            BuildManifestJson(hostPath),
            new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));

        return manifestPath;
    }

    public static void Register(string hostPath, string manifestDirectory, bool machineScope)
    {
        var manifestPath = WriteManifest(hostPath, manifestDirectory);

        foreach (var view in new[] { RegistryView.Registry64, RegistryView.Registry32 })
        {
            using var root = RegistryKey.OpenBaseKey(
                machineScope ? RegistryHive.LocalMachine : RegistryHive.CurrentUser,
                view);

            foreach (var suffix in BrowserKeySuffixes)
            {
                using var key = root.CreateSubKey(suffix, writable: true)
                    ?? throw new InvalidOperationException($"Cannot create registry key '{suffix}'.");
                key.SetValue(null, manifestPath, RegistryValueKind.String);
            }
        }
    }

    public static void Unregister(string manifestDirectory, bool machineScope)
    {
        foreach (var view in new[] { RegistryView.Registry64, RegistryView.Registry32 })
        {
            using var root = RegistryKey.OpenBaseKey(
                machineScope ? RegistryHive.LocalMachine : RegistryHive.CurrentUser,
                view);

            foreach (var suffix in BrowserKeySuffixes)
                root.DeleteSubKeyTree(suffix, throwOnMissingSubKey: false);
        }

        var manifestPath = GetManifestPath(manifestDirectory);
        if (File.Exists(manifestPath))
            File.Delete(manifestPath);
    }
}
