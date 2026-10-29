using System.Diagnostics;
using EduFocus.Ports.DTOs;
using EduFocus.Ports.Outbound.Services;

namespace EduFocus.Infrastructure.Watchdog;

/// <summary>
/// App-side liveness beacon. It writes immediately at startup, then on a fixed
/// interval, and removes the file on a clean exit so a stale beat never causes
/// the watchdog to misread a normal shutdown. Failures are logged, not thrown.
/// </summary>
public sealed class WatchdogHeartbeatPublisher : IAsyncDisposable
{
    private readonly IWatchdogHeartbeatStore _store;
    private readonly WatchdogHeartbeatOptions _options;
    private readonly IAppLogger _logger;
    private readonly TimeProvider _timeProvider;
    private readonly CancellationTokenSource _stopping = new();

    private Task? _loop;
    private long _processId;
    private DateTime _processStartedAtUtc;

    public WatchdogHeartbeatPublisher(
        IWatchdogHeartbeatStore store,
        WatchdogHeartbeatOptions options,
        IAppLogger logger,
        TimeProvider timeProvider)
    {
        _store = store ?? throw new ArgumentNullException(nameof(store));
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _timeProvider = timeProvider ?? throw new ArgumentNullException(nameof(timeProvider));

        if (options.Interval < TimeSpan.FromSeconds(1))
            throw new ArgumentOutOfRangeException(nameof(options), "The heartbeat interval must be at least one second.");
    }

    public void Start()
    {
        if (_loop is not null)
            return;

        using (var process = Process.GetCurrentProcess())
        {
            _processId = process.Id;

            try
            {
                _processStartedAtUtc = process.StartTime.ToUniversalTime();
            }
            catch (Exception ex) when (ex is InvalidOperationException or System.ComponentModel.Win32Exception)
            {
                _processStartedAtUtc = _timeProvider.GetUtcNow().UtcDateTime;
            }
        }

        _loop = Task.Run(() => RunAsync(_stopping.Token));
    }

    public async ValueTask DisposeAsync()
    {
        if (_loop is null)
        {
            _stopping.Dispose();
            return;
        }

        try
        {
            await _stopping.CancelAsync();

            var completed = await Task.WhenAny(_loop, Task.Delay(TimeSpan.FromSeconds(2)));
            if (completed != _loop)
                _logger.Warning(LogCategories.Watchdog, "The heartbeat publisher did not stop in time.");
        }
        catch (Exception ex)
        {
            _logger.Error(LogCategories.Watchdog, "Stopping the heartbeat publisher failed.", ex);
        }
        finally
        {
            _stopping.Dispose();
        }
    }

    private async Task RunAsync(CancellationToken cancellationToken)
    {
        using var timer = new PeriodicTimer(_options.Interval);

        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                await _store.WriteAsync(
                    new WatchdogHeartbeat(_processId, _processStartedAtUtc, _timeProvider.GetUtcNow().UtcDateTime),
                    cancellationToken);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.Error(LogCategories.Watchdog, "Could not write the app heartbeat.", ex);
            }

            try
            {
                if (!await timer.WaitForNextTickAsync(cancellationToken))
                    break;
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }

        try
        {
            await _store.DeleteAsync(CancellationToken.None);
        }
        catch (Exception ex)
        {
            _logger.Error(LogCategories.Watchdog, "Could not remove the app heartbeat on exit.", ex);
        }
    }
}
