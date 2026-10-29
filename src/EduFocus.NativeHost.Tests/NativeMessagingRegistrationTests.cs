using System.Text;
using System.Text.Json;
using FluentAssertions;

namespace EduFocus.NativeHost.Tests;

public sealed class NativeMessagingRegistrationTests
{
    private const string HostPath =
        @"C:\Program Files\EduFocus\native-host\EduFocus.NativeHost.exe";

    [Fact]
    public void Manifest_matches_the_chrome_native_messaging_contract()
    {
        using var document = JsonDocument.Parse(
            NativeMessagingRegistration.BuildManifestJson(HostPath));
        var root = document.RootElement;

        root.GetProperty("name").GetString().Should().Be("com.edufocus.native_host");
        root.GetProperty("type").GetString().Should().Be("stdio");
        root.GetProperty("path").GetString().Should().Be(HostPath);
        root.GetProperty("allowed_origins")
            .EnumerateArray()
            .Select(value => value.GetString())
            .Should()
            .ContainSingle("chrome-extension://lcanbfnejggilcaeinmahlcfimecmjmp/");
    }

    [Fact]
    public void Manifest_never_grants_origins_other_than_the_edufocus_extension()
    {
        using var document = JsonDocument.Parse(
            NativeMessagingRegistration.BuildManifestJson(HostPath));

        document.RootElement.GetProperty("allowed_origins")
            .GetArrayLength()
            .Should()
            .Be(1);
    }

    [Fact]
    public void WriteManifest_creates_utf8_json_without_a_bom()
    {
        var directory = Path.Combine(
            Path.GetTempPath(),
            "edufocus-native-host-" + Guid.NewGuid().ToString("N"));

        try
        {
            var path = NativeMessagingRegistration.WriteManifest(HostPath, directory);
            var bytes = File.ReadAllBytes(path);

            path.Should().Be(Path.Combine(directory, "com.edufocus.native_host.json"));
            var hasBom = bytes.Length >= 3
                && bytes[0] == 0xEF
                && bytes[1] == 0xBB
                && bytes[2] == 0xBF;
            hasBom.Should().BeFalse();
            JsonDocument.Parse(bytes).RootElement.GetProperty("name").GetString()
                .Should().Be(NativeMessagingRegistration.HostName);
        }
        finally
        {
            if (Directory.Exists(directory))
                Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public void Registry_keys_cover_chrome_and_edge_for_both_registry_views()
    {
        NativeMessagingRegistration.RegistryKeySuffixes.Should().BeEquivalentTo(
            [
                @"Software\Google\Chrome\NativeMessagingHosts\com.edufocus.native_host",
                @"Software\Microsoft\Edge\NativeMessagingHosts\com.edufocus.native_host"
            ]);
    }
}
