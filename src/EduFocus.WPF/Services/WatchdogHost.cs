using System.IO;
using EduFocus.Infrastructure.Watchdog;
using EduFocus.Ports.DTOs;
using EduFocus.Ports.Inbound;
using EduFocus.Ports.Outbound.Services;
using EduFocus.UseCases.Watchdog;

namespace EduFocus.WPF.Services;

/// <summary>
/// Resident watchdog loop. One instance per machine (named mutex); it ticks the
/// policy, reacts to stop requests and exits on shutdown. Repeated policy
/// failures exit the loop instead of spinning so the health scheduled task can
/// start a fresh supervisor.
/// </summary>
public sealed class WatchdogHost
{
    private static readonly TimeSpan FailureDelay = TimeSpan.FromSeconds(10);
    private static readonly TimeSpan DelaySlice = TimeSpan.FromMilliseconds(250);
    private const int MaxConsecutiveFailures = 5;

    private readonly IWatchdogUseCase _watchdog;
    private readonly WatchdogOptions _options;
    private readonly WatchdogRuntimeFiles _files;
    private readonly IAppLogger _logger;

    public WatchdogHost(
        IWatchdogUseCase watchdog,
        WatchdogOptions options,
        WatchdogRuntimeFiles files,
        IAppLogger logger)
    {
        _watchdog = watchdog ?? throw new ArgumentNullException(nameof(watchdog));
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _files = files ?? throw new ArgumentNullException(nameof(files));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>Runs until the watchdog should stop; returns when this instance is done.</summary>
    public async Task RunAsync(bool loginStart)
    {
        _files.EnsureDirectory();
        _files.TryDelete(_files.StopRequestPath);

        if (_files.IsShutdownInProgressForCurrentBoot())
        {
            _logger.Info(
                LogCategories.Watchdog,
                "Watchdog did not start because Windows shutdown for this boot is in progress.");
            return;
        }

        Mutex? mutex = null;
        var ownsMutex = false;

        try
        {
            try
            {
                mutex = new Mutex(
                    initiallyOwned: true,
                    WatchdogNames.WatchdogMutexForCurrentProcess,
                    out var createdNew);
                ownsMutex = createdNew;
            }
            catch (Exception ex) when (ex is UnauthorizedAccessException or IOException)
            {
                _logger.Error(LogCategories.Watchdog, "The watchdog mutex could not be created.", ex);
                return;
            }

            if (!ownsMutex)
            {
                _logger.Info(LogCategories.Watchdog, "Another watchdog instance is already running.");
                return;
            }

            using var stopEvent = TryCreateStopEvent();
            _files.WriteWatchdogPid(Environment.ProcessId);

            await RunLoopAsync(loginStart, stopEvent);
        }
        finally
        {
            _files.DeleteWatchdogPid();

            if (ownsMutex)
            {
                try
                {
                    mutex?.ReleaseMutex();
                }
                catch (ApplicationException)
                {
                    // The mutex was already abandoned or released.
                }
            }

            mutex?.Dispose();
        }
    }

    private async Task RunLoopAsync(bool loginStart, EventWaitHandle? stopEvent)
    {
        var failures = 0;
        var firstTick = true;

        _logger.Info(
            LogCategories.Watchdog,
            $"Watchdog loop started (pid={Environment.ProcessId}, login={loginStart}).");

        while (true)
        {
            if (Environment.HasShutdownStarted || IsStopRequested(stopEvent))
            {
                _logger.Info(LogCategories.Watchdog, "Watchdog loop stopped by a stop/shutdown signal.");
                return;
            }

            WatchdogTickResult result;

            try
            {
                result = await _watchdog.TickAsync(firstTick && loginStart);
                failures = 0;
            }
            catch (Exception ex)
            {
                failures++;
                _logger.Error(LogCategories.Watchdog, "A watchdog tick failed.", ex);

                if (failures >= MaxConsecutiveFailures)
                {
                    _logger.Error(
                        LogCategories.Watchdog,
                        "Watchdog is exiting after repeated tick failures; the health task will retry.");
                    return;
                }

                result = WatchdogTickResult.Wait(FailureDelay);
            }

            firstTick = false;

            if (result.Action is WatchdogTickAction.Stop
                or WatchdogTickAction.StartedAppAtLogin
                or WatchdogTickAction.GaveUp)
            {
                _logger.Info(
                    LogCategories.Watchdog,
                    $"Watchdog action '{result.Action}' ends this watchdog run.");
                return;
            }

            if (!await DelayAsync(result.Delay, stopEvent))
            {
                _logger.Info(LogCategories.Watchdog, "Watchdog loop stopped during its delay window.");
                return;
            }
        }
    }

    private async Task<bool> DelayAsync(TimeSpan delay, EventWaitHandle? stopEvent)
    {
        var deadline = DateTime.UtcNow + delay;

        while (true)
        {
            if (Environment.HasShutdownStarted || IsStopRequested(stopEvent))
                return false;

            var remaining = deadline - DateTime.UtcNow;
            if (remaining <= TimeSpan.Zero)
                return true;

            await Task.Delay(remaining < DelaySlice ? remaining : DelaySlice);
        }
    }

    private bool IsStopRequested(EventWaitHandle? stopEvent)
    {
        if (stopEvent?.WaitOne(0) == true)
            return true;

        if (!_files.HasStopRequest())
            return false;

        _files.TryDelete(_files.StopRequestPath);
        return true;
    }

    private EventWaitHandle? TryCreateStopEvent()
    {
        try
        {
            return new EventWaitHandle(
                initialState: false,
                EventResetMode.AutoReset,
                WatchdogNames.WatchdogStopEventForCurrentProcess);
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or IOException)
        {
            _logger.Warning(
                LogCategories.Watchdog,
                "The watchdog stop event is unavailable; stop requests still work through the stop file.");
            return null;
        }
    }
}
