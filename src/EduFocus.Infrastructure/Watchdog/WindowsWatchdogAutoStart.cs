using System.Runtime.Versioning;
using EduFocus.Ports.DTOs;
using EduFocus.Ports.Outbound.Services;

namespace EduFocus.Infrastructure.Watchdog;

/// <summary>
/// Registers two Task Scheduler entries with <c>schtasks</c>: one at logon and
/// one every minute. Each starts <c>EduFocus.exe --watchdog</c> with the highest
/// privileges; the resident watchdog mutex makes a second instance exit at
/// once, so the two tasks supervise each other without duplicate supervisors.
/// Registration is skipped when the app is not elevated, which also keeps
/// development builds from touching the machine.
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class WindowsWatchdogAutoStart : IWatchdogAutoStartPort
{
    private const string Schtasks = "schtasks.exe";

    private readonly WatchdogRuntimeOptions _options;
    private readonly IWatchdogCommandRunner _runner;
    private readonly IProcessElevationProbe _elevation;
    private readonly IAppLogger _logger;

    public WindowsWatchdogAutoStart(
        WatchdogRuntimeOptions options,
        IWatchdogCommandRunner runner,
        IProcessElevationProbe elevation,
        IAppLogger logger)
    {
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _runner = runner ?? throw new ArgumentNullException(nameof(runner));
        _elevation = elevation ?? throw new ArgumentNullException(nameof(elevation));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public async Task SynchronizeAsync(
        WatchdogRegistrationMode mode,
        CancellationToken cancellationToken = default)
    {
        if (!OperatingSystem.IsWindows())
            throw new PlatformNotSupportedException("Watchdog auto-start uses Windows Task Scheduler.");

        if (!_elevation.IsElevated)
        {
            _logger.Info(
                LogCategories.Watchdog,
                "Watchdog scheduled tasks were left unchanged because EduFocus is not running elevated.");
            return;
        }

        var executable = _options.ResolveExecutablePath();

        if (executable is null)
            throw new InvalidOperationException("The EduFocus executable could not be located for the watchdog tasks.");

        switch (mode)
        {
            case WatchdogRegistrationMode.None:
                await DeleteTaskAsync(WatchdogNames.LogonTaskName, cancellationToken);
                await DeleteTaskAsync(WatchdogNames.HealthTaskName, cancellationToken);
                break;

            case WatchdogRegistrationMode.LogonOnly:
                await CreateLogonTaskAsync(executable, cancellationToken);
                await DeleteTaskAsync(WatchdogNames.HealthTaskName, cancellationToken);
                break;

            case WatchdogRegistrationMode.LogonAndHealth:
                await CreateLogonTaskAsync(executable, cancellationToken);
                await CreateHealthTaskAsync(executable, cancellationToken);
                break;

            default:
                throw new ArgumentOutOfRangeException(nameof(mode), mode, "Unknown watchdog registration mode.");
        }
    }

    private Task CreateLogonTaskAsync(string executable, CancellationToken cancellationToken) =>
        CreateTaskAsync(
            WatchdogNames.LogonTaskName,
            BuildTaskAction(executable, "--watchdog --login"),
            ["/SC", "ONLOGON"],
            cancellationToken);

    private Task CreateHealthTaskAsync(string executable, CancellationToken cancellationToken) =>
        CreateTaskAsync(
            WatchdogNames.HealthTaskName,
            BuildTaskAction(executable, "--watchdog"),
            ["/SC", "MINUTE", "/MO", "1", "/ST", "00:00"],
            cancellationToken);

    private async Task CreateTaskAsync(
        string taskName,
        string taskAction,
        IReadOnlyList<string> schedule,
        CancellationToken cancellationToken)
    {
        var arguments = new List<string>
        {
            "/Create",
            "/TN", taskName,
            "/TR", taskAction
        };
        arguments.AddRange(schedule);
        arguments.AddRange(["/RL", "HIGHEST", "/F"]);

        var result = await _runner.RunAsync(Schtasks, arguments, cancellationToken);

        if (result.ExitCode != 0)
        {
            _logger.Error(
                LogCategories.Watchdog,
                $"Creating scheduled task '{taskName}' failed with exit code {result.ExitCode}: {result.StandardError.Trim()}");
            throw new InvalidOperationException(
                $"Creating watchdog task '{taskName}' failed with exit code {result.ExitCode}.");
        }

        _logger.Info(LogCategories.Watchdog, $"Scheduled task '{taskName}' registered.");
    }

    private async Task DeleteTaskAsync(string taskName, CancellationToken cancellationToken)
    {
        var result = await _runner.RunAsync(
            Schtasks,
            ["/Delete", "/TN", taskName, "/F"],
            cancellationToken);

        if (result.ExitCode != 0)
        {
            // A missing task is the normal case for a first install; a real
            // failure is harmless because a watchdog with protection OFF stops
            // itself on its first tick.
            _logger.Info(
                LogCategories.Watchdog,
                $"Scheduled task '{taskName}' was not deleted (exit code {result.ExitCode}); it may not exist.");
        }
    }

    /// <summary>The task action needs the quoted executable followed by its arguments.</summary>
    internal static string BuildTaskAction(string executable, string arguments) =>
        $"\"{executable}\" {arguments}";
}
