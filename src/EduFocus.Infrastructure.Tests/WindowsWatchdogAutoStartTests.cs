using System.Runtime.Versioning;
using EduFocus.Infrastructure.Watchdog;
using EduFocus.Ports.DTOs;
using EduFocus.Ports.Outbound.Services;
using FluentAssertions;

namespace EduFocus.Infrastructure.Tests;

[SupportedOSPlatform("windows")]
public sealed class WindowsWatchdogAutoStartTests : IDisposable
{
    private readonly string _root;
    private readonly string _executable;
    private readonly RecordingCommandRunner _runner = new();
    private readonly TestLogger _logger = new();

    public WindowsWatchdogAutoStartTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "edufocus-watchdog-autostart", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_root);
        _executable = Path.Combine(_root, "EduFocus.exe");
        File.WriteAllText(_executable, string.Empty);
    }

    [Fact]
    public async Task Logon_and_health_mode_registers_both_tasks()
    {
        await CreateAutoStart(elevated: true)
            .SynchronizeAsync(WatchdogRegistrationMode.LogonAndHealth);

        _runner.Calls.Should().HaveCount(2);

        var logon = _runner.Calls[0];
        logon.FileName.Should().Be("schtasks.exe");
        logon.Arguments.Should().Equal(
            "/Create",
            "/TN", WatchdogNames.LogonTaskName,
            "/TR", $"\"{_executable}\" --watchdog --login",
            "/SC", "ONLOGON",
            "/RL", "HIGHEST",
            "/F");

        var health = _runner.Calls[1];
        health.Arguments.Should().Equal(
            "/Create",
            "/TN", WatchdogNames.HealthTaskName,
            "/TR", $"\"{_executable}\" --watchdog",
            "/SC", "MINUTE",
            "/MO", "1",
            "/ST", "00:00",
            "/RL", "HIGHEST",
            "/F");
    }

    [Fact]
    public async Task Logon_only_mode_registers_logon_and_removes_the_health_task()
    {
        await CreateAutoStart(elevated: true)
            .SynchronizeAsync(WatchdogRegistrationMode.LogonOnly);

        _runner.Calls.Should().HaveCount(2);
        _runner.Calls[0].Arguments.Should().Contain("/Create");
        _runner.Calls[0].Arguments.Should().Contain(WatchdogNames.LogonTaskName);
        _runner.Calls[1].Arguments.Should().Equal(
            "/Delete",
            "/TN", WatchdogNames.HealthTaskName,
            "/F");
    }

    [Fact]
    public async Task None_mode_removes_both_tasks()
    {
        await CreateAutoStart(elevated: true)
            .SynchronizeAsync(WatchdogRegistrationMode.None);

        _runner.Calls.Should().HaveCount(2);
        _runner.Calls.Select(call => call.Arguments[2]).Should().Equal(
            WatchdogNames.LogonTaskName,
            WatchdogNames.HealthTaskName);
        _runner.Calls.Should().OnlyContain(call => call.Arguments[0] == "/Delete");
    }

    [Fact]
    public async Task A_missing_task_is_ignored_when_deleting()
    {
        _runner.Respond = _ => new WatchdogCommandResult(1, string.Empty, "The system cannot find the file specified.");
        var autoStart = CreateAutoStart(elevated: true);

        await autoStart.SynchronizeAsync(WatchdogRegistrationMode.None);

        _logger.Errors.Should().BeEmpty();
        _logger.Infos.Should().Contain(message => message.Contains("not deleted"));
    }

    [Fact]
    public async Task Registration_is_skipped_when_not_elevated()
    {
        await CreateAutoStart(elevated: false)
            .SynchronizeAsync(WatchdogRegistrationMode.LogonAndHealth);

        _runner.Calls.Should().BeEmpty();
        _logger.Infos.Should().ContainSingle(message => message.Contains("not running elevated"));
    }

    [Fact]
    public async Task A_failed_create_reports_the_error_and_throws()
    {
        _runner.Respond = arguments => arguments.Contains("/Create")
            ? new WatchdogCommandResult(5, string.Empty, "Access is denied.")
            : new WatchdogCommandResult(0, string.Empty, string.Empty);
        var autoStart = CreateAutoStart(elevated: true);

        var act = () => autoStart.SynchronizeAsync(WatchdogRegistrationMode.LogonOnly);

        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage($"*'{WatchdogNames.LogonTaskName}'*");
        _logger.Errors.Should().ContainSingle(message => message.Contains("Access is denied."));
    }

    [Fact]
    public async Task A_missing_executable_throws_before_touching_the_scheduler()
    {
        var options = new WatchdogRuntimeOptions
        {
            RuntimeDirectory = _root,
            ExecutablePath = Path.Combine(_root, "missing", "EduFocus.exe")
        };
        var autoStart = new WindowsWatchdogAutoStart(options, _runner, new FakeElevationProbe(true), _logger);

        var act = () => autoStart.SynchronizeAsync(WatchdogRegistrationMode.LogonAndHealth);

        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*could not be located*");
        _runner.Calls.Should().BeEmpty();
    }

    [Fact]
    public async Task An_unknown_mode_is_rejected()
    {
        var autoStart = CreateAutoStart(elevated: true);

        var act = () => autoStart.SynchronizeAsync((WatchdogRegistrationMode)42);

        await act.Should().ThrowAsync<ArgumentOutOfRangeException>();
        _runner.Calls.Should().BeEmpty();
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

    private WindowsWatchdogAutoStart CreateAutoStart(bool elevated) =>
        new(
            new WatchdogRuntimeOptions { RuntimeDirectory = _root, ExecutablePath = _executable },
            _runner,
            new FakeElevationProbe(elevated),
            _logger);

    private sealed class FakeElevationProbe(bool elevated) : IProcessElevationProbe
    {
        public bool IsElevated { get; } = elevated;
    }

    private sealed class RecordingCommandRunner : IWatchdogCommandRunner
    {
        public List<(string FileName, IReadOnlyList<string> Arguments)> Calls { get; } = [];

        public Func<IReadOnlyList<string>, WatchdogCommandResult> Respond { get; set; } =
            _ => new WatchdogCommandResult(0, string.Empty, string.Empty);

        public Task<WatchdogCommandResult> RunAsync(
            string fileName,
            IReadOnlyList<string> arguments,
            CancellationToken cancellationToken = default)
        {
            Calls.Add((fileName, arguments.ToList()));
            return Task.FromResult(Respond(arguments));
        }
    }
}
