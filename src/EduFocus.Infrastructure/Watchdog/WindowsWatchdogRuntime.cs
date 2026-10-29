using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.Versioning;
using EduFocus.Ports.Outbound.Services;

namespace EduFocus.Infrastructure.Watchdog;

/// <summary>
/// App-side control of the resident watchdog. A stop request is written to a
/// file (works even when the named event is unavailable) and mirrored through a
/// named event so the watchdog reacts during its delay window too.
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class WindowsWatchdogRuntime : IWatchdogRuntimePort
{
    private static readonly TimeSpan StartupWait = TimeSpan.FromSeconds(5);
    private static readonly TimeSpan PollInterval = TimeSpan.FromMilliseconds(100);

    private readonly WatchdogRuntimeOptions _options;
    private readonly WatchdogRuntimeFiles _files;
    private readonly IAppLogger _logger;

    public WindowsWatchdogRuntime(
        WatchdogRuntimeOptions options,
        WatchdogRuntimeFiles files,
        IAppLogger logger)
    {
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _files = files ?? throw new ArgumentNullException(nameof(files));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public bool IsRunning
    {
        get
        {
            try
            {
                if (Mutex.TryOpenExisting(WatchdogNames.WatchdogMutexForCurrentProcess, out var mutex))
                {
                    mutex.Dispose();
                    return true;
                }

                return false;
            }
            catch (UnauthorizedAccessException)
            {
                return true;
            }
            catch (Exception ex) when (ex is IOException or WaitHandleCannotBeOpenedException)
            {
                return false;
            }
        }
    }

    public bool EnsureStarted()
    {
        if (IsRunning)
            return true;

        // A live app asking for its supervisor means the session is running, so
        // any shutdown marker from an earlier session-ending attempt is stale.
        _files.ClearShutdownFlag();
        _files.TryDelete(_files.StopRequestPath);

        var executable = _options.ResolveExecutablePath();

        if (executable is null)
        {
            _logger.Error(LogCategories.Watchdog, "The EduFocus executable could not be located to start the watchdog.");
            return false;
        }

        try
        {
            var startInfo = new ProcessStartInfo(executable)
            {
                UseShellExecute = false,
                CreateNoWindow = true,
                WorkingDirectory = Path.GetDirectoryName(executable) ?? AppContext.BaseDirectory
            };
            startInfo.ArgumentList.Add("--watchdog");

            using var process = Process.Start(startInfo);

            if (process is null)
            {
                _logger.Error(LogCategories.Watchdog, "Windows did not start the watchdog process.");
                return false;
            }
        }
        catch (Exception ex) when (ex is Win32Exception or InvalidOperationException or FileNotFoundException)
        {
            _logger.Error(LogCategories.Watchdog, "Starting the watchdog process failed.", ex);
            return false;
        }

        var deadline = DateTime.UtcNow + StartupWait;

        while (DateTime.UtcNow < deadline)
        {
            if (IsRunning)
                return true;

            Thread.Sleep(PollInterval);
        }

        _logger.Error(LogCategories.Watchdog, "The watchdog process did not signal readiness in time.");
        return false;
    }

    public void NotifySessionEnding()
    {
        _files.MarkShutdownInProgress();
        _files.RequestStop();

        if (TrySignalStopEvent())
            _logger.Info(LogCategories.Watchdog, "The watchdog was told that Windows is shutting down.");
    }

    public async Task<bool> RequestStopAsync(
        TimeSpan timeout,
        CancellationToken cancellationToken = default)
    {
        if (!IsRunning)
        {
            _files.TryDelete(_files.StopRequestPath);
            return true;
        }

        _files.RequestStop();
        TrySignalStopEvent();

        var deadline = DateTime.UtcNow + timeout;

        while (DateTime.UtcNow < deadline)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (!IsRunning)
            {
                _files.TryDelete(_files.StopRequestPath);
                return true;
            }

            try
            {
                await Task.Delay(PollInterval, cancellationToken);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
        }

        return !IsRunning;
    }

    private static bool TrySignalStopEvent()
    {
        try
        {
            if (!EventWaitHandle.TryOpenExisting(
                    WatchdogNames.WatchdogStopEventForCurrentProcess,
                    out var handle))
            {
                return false;
            }

            using (handle)
            {
                handle.Set();
                return true;
            }
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or IOException or WaitHandleCannotBeOpenedException)
        {
            return false;
        }
    }
}
