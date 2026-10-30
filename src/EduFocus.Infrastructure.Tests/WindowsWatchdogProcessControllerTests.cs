using System.Diagnostics;
using System.Runtime.Versioning;
using EduFocus.Infrastructure.Watchdog;
using EduFocus.Ports.Outbound.Services;
using FluentAssertions;

namespace EduFocus.Infrastructure.Tests;

[SupportedOSPlatform("windows")]
public sealed class WindowsWatchdogProcessControllerTests
{
    private readonly WindowsWatchdogProcessController _controller = new(
        new WatchdogRuntimeOptions
        {
            RuntimeDirectory = Path.Combine(Path.GetTempPath(), "edufocus-watchdog-process"),
            ExecutablePath = Path.Combine(Path.GetTempPath(), "edufocus-watchdog-process", "EduFocus.exe")
        },
        new TestLogger());

    [Fact]
    public void The_current_process_is_alive_when_its_start_time_matches()
    {
        using var process = Process.GetCurrentProcess();

        _controller
            .IsProcessAlive(process.Id, process.StartTime.ToUniversalTime())
            .Should()
            .BeTrue();
    }

    [Fact]
    public void A_recycled_pid_with_a_different_start_time_is_not_alive()
    {
        using var process = Process.GetCurrentProcess();

        _controller
            .IsProcessAlive(process.Id, process.StartTime.ToUniversalTime().AddMinutes(-5))
            .Should()
            .BeFalse();
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-3)]
    public void Invalid_pids_are_never_alive(long processId)
    {
        _controller.IsProcessAlive(processId, DateTime.UtcNow).Should().BeFalse();
    }

    [Fact]
    public void A_pid_that_does_not_exist_is_not_alive()
    {
        _controller.IsProcessAlive(int.MaxValue, DateTime.UtcNow).Should().BeFalse();
    }

    [Fact]
    public void A_held_app_mutex_counts_as_a_running_instance()
    {
        using var mutex = new Mutex(initiallyOwned: true, WatchdogNames.AppInstanceMutex);
        try
        {
            _controller.IsAppInstanceRunning().Should().BeTrue();
        }
        finally
        {
            mutex.ReleaseMutex();
        }
    }
}
