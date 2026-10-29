using EduFocus.Infrastructure.Watchdog;
using FluentAssertions;

namespace EduFocus.Infrastructure.Tests;

public sealed class WatchdogCommandLineTests
{
    [Fact]
    public void No_arguments_run_the_normal_app()
    {
        var command = WatchdogCommandLine.Parse([]);

        command.Mode.Should().Be(WatchdogCommandMode.App);
        command.LoginStart.Should().BeFalse();
    }

    [Fact]
    public void Watchdog_mode_is_recognized_case_insensitively()
    {
        var command = WatchdogCommandLine.Parse(["--WATCHDOG"]);

        command.Mode.Should().Be(WatchdogCommandMode.Watchdog);
        command.LoginStart.Should().BeFalse();
    }

    [Fact]
    public void Login_is_only_attached_to_the_watchdog_mode()
    {
        var command = WatchdogCommandLine.Parse(["--watchdog", "--login"]);

        command.Mode.Should().Be(WatchdogCommandMode.Watchdog);
        command.LoginStart.Should().BeTrue();
    }

    [Fact]
    public void Stop_mode_reads_the_wait_timeout()
    {
        var command = WatchdogCommandLine.Parse(["--watchdog-stop", "--wait-ms", "1500"]);

        command.Mode.Should().Be(WatchdogCommandMode.Stop);
        command.WaitTimeout.Should().Be(TimeSpan.FromMilliseconds(1500));
    }

    [Fact]
    public void Uninstall_mode_defaults_to_five_seconds()
    {
        var command = WatchdogCommandLine.Parse(["--uninstall-watchdog"]);

        command.Mode.Should().Be(WatchdogCommandMode.Uninstall);
        command.WaitTimeout.Should().Be(TimeSpan.FromMilliseconds(WatchdogCommandLine.DefaultWaitMilliseconds));
    }

    [Theory]
    [InlineData("999999", 60000)]
    [InlineData("-50", 0)]
    public void Wait_timeout_is_clamped(string raw, int expectedMilliseconds)
    {
        var command = WatchdogCommandLine.Parse(["--watchdog-stop", "--wait-ms", raw]);

        command.WaitTimeout.Should().Be(TimeSpan.FromMilliseconds(expectedMilliseconds));
    }

    [Fact]
    public void Invalid_wait_timeout_falls_back_to_the_default()
    {
        var command = WatchdogCommandLine.Parse(["--watchdog-stop", "--wait-ms", "soon"]);

        command.WaitTimeout.Should().Be(TimeSpan.FromMilliseconds(WatchdogCommandLine.DefaultWaitMilliseconds));
    }

    [Fact]
    public void Unknown_arguments_run_the_normal_app()
    {
        var command = WatchdogCommandLine.Parse(["--some-flag", "value"]);

        command.Mode.Should().Be(WatchdogCommandMode.App);
    }
}
