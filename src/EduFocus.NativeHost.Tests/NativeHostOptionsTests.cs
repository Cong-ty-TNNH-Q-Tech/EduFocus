using FluentAssertions;

namespace EduFocus.NativeHost.Tests;

public sealed class NativeHostOptionsTests
{
    private const string DefaultDirectory = @"C:\Program Files\EduFocus\native-host";

    [Fact]
    public void Chrome_origin_argument_keeps_relay_mode()
    {
        var options = NativeHostOptions.Parse(
            ["chrome-extension://lcanbfnejggilcaeinmahlcfimecmjmp/"],
            DefaultDirectory);

        options.Mode.Should().Be(NativeHostMode.Relay);
        options.ManifestDirectory.Should().Be(DefaultDirectory);
    }

    [Fact]
    public void Register_defaults_to_user_scope_and_the_host_directory()
    {
        var options = NativeHostOptions.Parse(["--register"], DefaultDirectory);

        options.Mode.Should().Be(NativeHostMode.Register);
        options.MachineScope.Should().BeFalse();
        options.ManifestDirectory.Should().Be(DefaultDirectory);
    }

    [Fact]
    public void Unregister_reads_machine_scope_and_manifest_directory()
    {
        var options = NativeHostOptions.Parse(
            ["--unregister", "--scope", "machine", "--manifest-dir", @"D:\Apps\EduFocus\native-host"],
            DefaultDirectory);

        options.Mode.Should().Be(NativeHostMode.Unregister);
        options.MachineScope.Should().BeTrue();
        options.ManifestDirectory.Should().Be(@"D:\Apps\EduFocus\native-host");
    }

    [Theory]
    [InlineData("--scope")]
    [InlineData("--scope", "everyone")]
    [InlineData("--manifest-dir")]
    public void Invalid_arguments_fail_instead_of_silently_registering(params string[] args)
    {
        var parse = () => NativeHostOptions.Parse(
            args.Prepend("--register").ToArray(),
            DefaultDirectory);

        parse.Should().Throw<ArgumentException>();
    }
}
