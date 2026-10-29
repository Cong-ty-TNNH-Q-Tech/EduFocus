using System.Globalization;
using EduFocus.Infrastructure.Watchdog;
using FluentAssertions;

namespace EduFocus.Infrastructure.Tests;

public sealed class WatchdogRuntimeFilesTests : IDisposable
{
    private readonly string _root;
    private readonly WatchdogRuntimeFiles _files;

    public WatchdogRuntimeFilesTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "edufocus-watchdog-tests", Guid.NewGuid().ToString("N"));
        _files = new WatchdogRuntimeFiles(new WatchdogRuntimeOptions { RuntimeDirectory = _root });
    }

    [Fact]
    public void Atomic_write_replaces_the_previous_content()
    {
        _files.WriteAtomic(_files.HeartbeatPath, "one");
        _files.WriteAtomic(_files.HeartbeatPath, "two");

        _files.TryReadAllText(_files.HeartbeatPath).Should().Be("two");
        File.Exists($"{_files.HeartbeatPath}.{Environment.ProcessId}.tmp").Should().BeFalse();
    }

    [Fact]
    public void Stop_request_round_trips()
    {
        _files.HasStopRequest().Should().BeFalse();

        _files.RequestStop();

        _files.HasStopRequest().Should().BeTrue();
        _files.TryReadAllText(_files.StopRequestPath).Should().Be(Environment.ProcessId.ToString(CultureInfo.InvariantCulture));
        _files.TryDelete(_files.StopRequestPath).Should().BeTrue();
        _files.HasStopRequest().Should().BeFalse();
    }

    [Fact]
    public void Shutdown_flag_is_valid_on_the_same_boot()
    {
        _files.MarkShutdownInProgress();

        _files.IsShutdownInProgressForCurrentBoot().Should().BeTrue();
        File.Exists(_files.ShutdownFlagPath).Should().BeTrue();
    }

    [Fact]
    public void Shutdown_flag_from_a_previous_boot_is_discarded()
    {
        Directory.CreateDirectory(_root);
        File.WriteAllText(
            _files.ShutdownFlagPath,
            (DateTime.UtcNow - TimeSpan.FromDays(2)).ToString("O", CultureInfo.InvariantCulture));

        _files.IsShutdownInProgressForCurrentBoot().Should().BeFalse();
        File.Exists(_files.ShutdownFlagPath).Should().BeFalse();
    }

    [Fact]
    public void Unreadable_shutdown_flag_is_ignored()
    {
        Directory.CreateDirectory(_root);
        File.WriteAllText(_files.ShutdownFlagPath, "not-a-date");

        _files.IsShutdownInProgressForCurrentBoot().Should().BeFalse();
        File.Exists(_files.ShutdownFlagPath).Should().BeFalse();
    }

    [Fact]
    public void Delete_all_removes_every_runtime_file()
    {
        _files.WriteWatchdogPid(123);
        _files.RequestStop();
        _files.MarkShutdownInProgress();
        _files.WriteAtomic(_files.HeartbeatPath, "beat");
        _files.WriteAtomic(_files.StatePath, "state");

        _files.DeleteAll();

        File.Exists(_files.PidPath).Should().BeFalse();
        File.Exists(_files.StopRequestPath).Should().BeFalse();
        File.Exists(_files.ShutdownFlagPath).Should().BeFalse();
        File.Exists(_files.HeartbeatPath).Should().BeFalse();
        File.Exists(_files.StatePath).Should().BeFalse();
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_root))
                Directory.Delete(_root, recursive: true);
        }
        catch (IOException)
        {
            // Test cleanup is best effort.
        }
    }
}
