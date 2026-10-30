using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.Versioning;
using EduFocus.Ports.DTOs;
using EduFocus.Ports.Outbound.Services;

namespace EduFocus.Infrastructure.Watchdog;

/// <summary>
/// Windows implementation of the watchdog's process seam. A process only counts
/// as the app when both its id and start time match the heartbeat, so a recycled
/// pid is never killed or mistaken for a live EduFocus instance.
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class WindowsWatchdogProcessController : IWatchdogProcessPort
{
    private static readonly TimeSpan StartTimeTolerance = TimeSpan.FromSeconds(2);
    private static readonly TimeSpan KillWait = TimeSpan.FromSeconds(5);

    private readonly WatchdogRuntimeOptions _options;
    private readonly IAppLogger _logger;

    public WindowsWatchdogProcessController(WatchdogRuntimeOptions options, IAppLogger logger)
    {
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public bool IsAppInstanceRunning()
    {
        try
        {
            if (Mutex.TryOpenExisting(WatchdogNames.AppInstanceMutex, out var mutex))
            {
                mutex.Dispose();
                return true;
            }

            return false;
        }
        catch (UnauthorizedAccessException)
        {
            // The mutex exists but cannot be opened; treating it as running
            // avoids starting a duplicate instance.
            return true;
        }
        catch (Exception ex) when (ex is IOException or WaitHandleCannotBeOpenedException)
        {
            return false;
        }
    }

    public bool IsProcessAlive(long processId, DateTime processStartedAtUtc)
    {
        if (processId is <= 0 or > int.MaxValue)
            return false;

        Process process;
        try
        {
            process = Process.GetProcessById((int)processId);
        }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException)
        {
            return false;
        }

        using (process)
        {
            try
            {
                if (process.HasExited)
                    return false;

                var started = process.StartTime.ToUniversalTime();
                return (started - processStartedAtUtc.ToUniversalTime()).Duration() <= StartTimeTolerance;
            }
            catch (Exception ex) when (ex is InvalidOperationException or Win32Exception or NotSupportedException)
            {
                return false;
            }
        }
    }

    public bool TryKill(long processId, DateTime processStartedAtUtc)
    {
        if (!IsProcessAlive(processId, processStartedAtUtc))
            return false;

        Process process;
        try
        {
            process = Process.GetProcessById((int)processId);
        }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException)
        {
            return true;
        }

        using (process)
        {
            try
            {
                process.Kill(entireProcessTree: true);
                return process.WaitForExit(KillWait);
            }
            catch (Exception ex) when (ex is InvalidOperationException or Win32Exception or NotSupportedException)
            {
                return false;
            }
        }
    }

    public WatchdogProcessInfo? StartApp()
    {
        var executable = _options.ResolveExecutablePath();

        if (executable is null)
        {
            _logger.Error(LogCategories.Watchdog, "The EduFocus executable could not be located for a watchdog restart.");
            return null;
        }

        try
        {
            var startInfo = new ProcessStartInfo(executable)
            {
                UseShellExecute = false,
                CreateNoWindow = true,
                WorkingDirectory = Path.GetDirectoryName(executable) ?? AppContext.BaseDirectory
            };

            using var process = Process.Start(startInfo);

            if (process is null)
            {
                _logger.Error(LogCategories.Watchdog, "Windows did not start a new EduFocus process.");
                return null;
            }

            DateTime startedAtUtc;
            try
            {
                startedAtUtc = process.StartTime.ToUniversalTime();
            }
            catch (Exception ex) when (ex is InvalidOperationException or Win32Exception)
            {
                startedAtUtc = DateTime.UtcNow;
            }

            return new WatchdogProcessInfo(process.Id, startedAtUtc);
        }
        catch (Exception ex) when (ex is Win32Exception or InvalidOperationException or FileNotFoundException)
        {
            _logger.Error(LogCategories.Watchdog, "Starting EduFocus from the watchdog failed.", ex);
            return null;
        }
    }
}
