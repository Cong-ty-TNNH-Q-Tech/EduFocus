using EduFocus.Domain.ValueObjects;
using EduFocus.Ports.Inbound;
using EduFocus.Ports.Outbound.Persistence;
using EduFocus.Ports.Outbound.Services;

namespace EduFocus.UseCases.Sessions;

/// <summary>
/// Writes the StudySession heartbeat every 10 seconds (UC07). ActiveSeconds is
/// derived from the monotonic clock, capped per beat, so wall-clock jumps and
/// suspended periods cannot inflate or invert the duration. A failed database
/// write is retried on the next beat; the loop keeps running until the session
/// ends or <see cref="StopAsync"/> flushes the final beat.
/// </summary>
public sealed class StudySessionHeartbeatService : IStudySessionHeartbeatUseCase, IAsyncDisposable
{
    private const string EndedMessage =
        "Heartbeat stopped because the study session is no longer active.";

    private const string WriteWarning =
        "Could not write the study-session heartbeat; the next beat will retry.";

    private readonly IStudySessionRepository _sessions;
    private readonly IMonotonicClockPort _clock;
    private readonly IAppLogger _logger;
    private readonly TimeProvider _timeProvider;
    private readonly StudySessionHeartbeatOptions _options;
    private readonly SemaphoreSlim _lifecycleGate = new(1, 1);
    private readonly SemaphoreSlim _beatGate = new(1, 1);

    private CancellationTokenSource? _loopCts;
    private Task? _loop;
    private StudySessionId? _sessionId;
    private DateTime _lastHeartbeatUtc;
    private TimeSpan _lastTick;
    private bool _hasAnchor;
    private volatile bool _running;

    public StudySessionHeartbeatService(
        IStudySessionRepository sessions,
        IMonotonicClockPort clock,
        IAppLogger logger,
        TimeProvider timeProvider,
        StudySessionHeartbeatOptions options)
    {
        _sessions = sessions ?? throw new ArgumentNullException(nameof(sessions));
        _clock = clock ?? throw new ArgumentNullException(nameof(clock));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _timeProvider = timeProvider ?? throw new ArgumentNullException(nameof(timeProvider));

        if (options is null)
            throw new ArgumentNullException(nameof(options));
        if (options.Interval <= TimeSpan.Zero || options.Interval > TimeSpan.FromMinutes(5))
            throw new ArgumentOutOfRangeException(nameof(options), "Interval must be between 0 and 5 minutes.");
        if (options.MaxActiveDelta < options.Interval || options.MaxActiveDelta > TimeSpan.FromDays(1))
            throw new ArgumentOutOfRangeException(
                nameof(options),
                "MaxActiveDelta must be at least the interval and at most 1 day.");

        _options = options;
    }

    public bool IsRunning => _running;

    public async Task StartAsync(
        StudySessionId sessionId,
        DateTime lastHeartbeatAtUtc,
        CancellationToken cancellationToken = default)
    {
        if (sessionId.IsEmpty)
            throw new ArgumentException("A session id is required.", nameof(sessionId));
        if (lastHeartbeatAtUtc.Kind != DateTimeKind.Utc)
            throw new ArgumentException("The last heartbeat must be UTC.", nameof(lastHeartbeatAtUtc));

        await _lifecycleGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await StopCoreAsync().ConfigureAwait(false);

            _sessionId = sessionId;
            _lastHeartbeatUtc = lastHeartbeatAtUtc;
            _lastTick = _clock.GetTickCount();
            _hasAnchor = true;
            _running = true;

            var cts = new CancellationTokenSource();
            _loopCts = cts;
            _loop = Task.Run(() => LoopAsync(cts.Token), CancellationToken.None);
        }
        finally
        {
            _lifecycleGate.Release();
        }
    }

    public async Task StopAsync(CancellationToken cancellationToken = default)
    {
        await _lifecycleGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await StopCoreAsync().ConfigureAwait(false);
        }
        finally
        {
            _lifecycleGate.Release();
        }
    }

    public async Task<bool> BeatOnceAsync(CancellationToken cancellationToken = default)
    {
        await _beatGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            return await BeatCoreAsync(cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _beatGate.Release();
        }
    }

    public async ValueTask DisposeAsync()
    {
        await StopAsync(CancellationToken.None).ConfigureAwait(false);
        _lifecycleGate.Dispose();
        _beatGate.Dispose();
    }

    private async Task StopCoreAsync()
    {
        var cts = _loopCts;
        var loop = _loop;
        _loopCts = null;
        _loop = null;

        cts?.Cancel();

        if (loop is not null)
        {
            try
            {
                await loop.ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
            }
            catch (Exception ex)
            {
                _logger.Error(LogCategories.Session, "The heartbeat loop stopped unexpectedly.", ex);
            }
        }

        cts?.Dispose();

        if (_hasAnchor && _sessionId is { } sessionId)
        {
            await _beatGate.WaitAsync(CancellationToken.None).ConfigureAwait(false);
            try
            {
                await BeatCoreAsync(CancellationToken.None).ConfigureAwait(false);
            }
            finally
            {
                _beatGate.Release();
            }
        }

        _hasAnchor = false;
        _sessionId = null;
        _running = false;
    }

    private async Task LoopAsync(CancellationToken cancellationToken)
    {
        try
        {
            using var timer = new PeriodicTimer(_options.Interval);

            while (await timer.WaitForNextTickAsync(cancellationToken).ConfigureAwait(false))
            {
                bool stillActive;

                await _beatGate.WaitAsync(cancellationToken).ConfigureAwait(false);
                try
                {
                    stillActive = await BeatCoreAsync(cancellationToken).ConfigureAwait(false);

                    if (!stillActive)
                    {
                        _hasAnchor = false;
                        _sessionId = null;
                    }
                }
                finally
                {
                    _beatGate.Release();
                }

                if (!stillActive)
                    break;
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
        catch (Exception ex)
        {
            _logger.Error(LogCategories.Session, "The heartbeat loop stopped unexpectedly.", ex);
        }
        finally
        {
            _running = false;
        }
    }

    private async Task<bool> BeatCoreAsync(CancellationToken cancellationToken)
    {
        if (!_hasAnchor || _sessionId is not { } sessionId)
            return false;

        var tick = _clock.GetTickCount();
        var elapsed = tick - _lastTick;
        var seconds = elapsed > TimeSpan.Zero
            ? (int)Math.Min(Math.Floor(elapsed.TotalSeconds), Math.Floor(_options.MaxActiveDelta.TotalSeconds))
            : 0;

        var now = _timeProvider.GetUtcNow().UtcDateTime;

        // A backward wall clock must not move LastHeartbeatAtUtc backwards; the
        // active delta still comes from the monotonic clock.
        if (now < _lastHeartbeatUtc)
            now = _lastHeartbeatUtc;

        try
        {
            var written = await _sessions
                .RecordHeartbeatAsync(sessionId, now, seconds, cancellationToken)
                .ConfigureAwait(false);

            if (!written)
            {
                _logger.Warning(LogCategories.Session, EndedMessage);
                return false;
            }

            _lastTick = tick;
            _lastHeartbeatUtc = now;
            return true;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.Warning(
                LogCategories.Session,
                $"{WriteWarning} ({ex.GetType().Name})");
            return true;
        }
    }
}
