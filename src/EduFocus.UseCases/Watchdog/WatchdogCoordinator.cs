using EduFocus.Ports.DTOs;
using EduFocus.Ports.Inbound;
using EduFocus.Ports.Outbound.Persistence;
using EduFocus.Ports.Outbound.Services;

namespace EduFocus.UseCases.Watchdog;

/// <summary>
/// Aligns the watchdog process and the two scheduled tasks with the persisted
/// settings: tasks exist when the app should return at login, the resident
/// watchdog only while protection is ON. Every failure is logged and ignored so
/// a watchdog problem can never block protection or a clean exit.
/// </summary>
public sealed class WatchdogCoordinator : IWatchdogCoordinator
{
    private readonly IAppSettingsRepository _settings;
    private readonly IWatchdogAutoStartPort _autoStart;
    private readonly IWatchdogRuntimePort _runtime;
    private readonly IAppLogger _logger;
    private readonly WatchdogOptions _options;
    private readonly SemaphoreSlim _gate = new(1, 1);

    private WatchdogRegistrationMode? _registeredMode;

    public WatchdogCoordinator(
        IAppSettingsRepository settings,
        IWatchdogAutoStartPort autoStart,
        IWatchdogRuntimePort runtime,
        IAppLogger logger,
        WatchdogOptions options)
    {
        _settings = settings ?? throw new ArgumentNullException(nameof(settings));
        _autoStart = autoStart ?? throw new ArgumentNullException(nameof(autoStart));
        _runtime = runtime ?? throw new ArgumentNullException(nameof(runtime));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _options = options ?? throw new ArgumentNullException(nameof(options));
    }

    public async Task SynchronizeAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            await _gate.WaitAsync(cancellationToken);
        }
        catch (OperationCanceledException)
        {
            return;
        }

        try
        {
            var settings = await _settings.GetAsync(cancellationToken);
            var onboardingCompleted = settings?.OnboardingCompleted == true;
            var protectionEnabled = settings?.ProtectionEnabled == true;
            var startWithWindows = settings?.StartWithWindows == true;

            var mode = !onboardingCompleted
                ? WatchdogRegistrationMode.None
                : protectionEnabled
                    ? WatchdogRegistrationMode.LogonAndHealth
                    : startWithWindows
                        ? WatchdogRegistrationMode.LogonOnly
                        : WatchdogRegistrationMode.None;

            await TryRegisterAsync(mode, cancellationToken);

            if (protectionEnabled)
            {
                if (!_runtime.IsRunning && !_runtime.EnsureStarted())
                {
                    _logger.Warning(
                        LogCategories.Watchdog,
                        "The watchdog process could not be started; protection is still ON.");
                }
            }
            else if (_runtime.IsRunning)
            {
                var stopped = await _runtime.RequestStopAsync(_options.StopWait, cancellationToken);

                if (!stopped)
                    _logger.Warning(
                        LogCategories.Watchdog,
                        "The watchdog process did not stop in time; it will exit on its next tick.");
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
        catch (Exception ex)
        {
            _logger.Error(
                LogCategories.Watchdog,
                "Watchdog synchronization failed; protection and sessions are unaffected.",
                ex);
        }
        finally
        {
            _gate.Release();
        }
    }

    private async Task TryRegisterAsync(
        WatchdogRegistrationMode mode,
        CancellationToken cancellationToken)
    {
        if (_registeredMode == mode)
            return;

        try
        {
            await _autoStart.SynchronizeAsync(mode, cancellationToken);
            _registeredMode = mode;
            _logger.Info(
                LogCategories.Watchdog,
                $"Watchdog auto-start tasks synchronized to '{mode}'.");
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.Error(
                LogCategories.Watchdog,
                $"Could not synchronize watchdog auto-start tasks to '{mode}'.",
                ex);
        }
    }
}

/// <summary>
/// Used when the Windows Service owns protection (<c>serviceManaged == true</c>)
/// so saving settings in Parent Mode never re-registers legacy user-mode watchdog
/// scheduled tasks or spawns a duplicate <c>--watchdog</c> supervisor.
/// </summary>
public sealed class NoOpWatchdogCoordinator : IWatchdogCoordinator
{
    public Task SynchronizeAsync(CancellationToken cancellationToken = default) =>
        Task.CompletedTask;
}
