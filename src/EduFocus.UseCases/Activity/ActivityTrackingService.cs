using EduFocus.Domain.Activity;
using EduFocus.Domain.Entities;
using EduFocus.Domain.Enums;
using EduFocus.Domain.Privacy;
using EduFocus.Domain.Protection;
using EduFocus.Domain.ValueObjects;
using EduFocus.Ports.Inbound;
using EduFocus.Ports.Outbound.Persistence;
using EduFocus.Ports.Outbound.Services;

namespace EduFocus.UseCases.Activity;

/// <summary>
/// Samples the foreground window for the active StudySession (UC07, issue #26).
/// Polling runs every second but a row is written at most once per sample
/// interval, and only after the foreground context has been stable for the
/// debounce period. Idle periods above the threshold produce a single sample.
/// Durations come from the monotonic clock and are capped per sample so sleep
/// or suspend cannot inflate category totals. Persistence writes the targeted
/// session counters before the sample row; database failures are logged and
/// retried on a later beat.
/// </summary>
public sealed class ActivityTrackingService : IActivityUseCase, IAsyncDisposable
{
    private const string SessionEndedMessage =
        "Activity sampling stopped because the study session is no longer active.";

    private const string WriteWarning =
        "Could not persist an activity sample; the next beat will retry.";

    private readonly IActivitySampleRepository _samples;
    private readonly IStudySessionRepository _sessions;
    private readonly IBlockRuleRepository _rules;
    private readonly IForegroundActivityPort _foreground;
    private readonly IBrowserUrlProviderPort _urls;
    private readonly IMonotonicClockPort _clock;
    private readonly IAppLogger _logger;
    private readonly TimeProvider _timeProvider;
    private readonly ActivityTrackingOptions _options;
    private readonly SemaphoreSlim _lifecycleGate = new(1, 1);
    private readonly SemaphoreSlim _sampleGate = new(1, 1);

    private CancellationTokenSource? _loopCts;
    private Task? _loop;
    private StudySessionId? _sessionId;
    private BlockRuleSet _ruleSnapshot = BlockRuleSet.Create([]);
    private TimeSpan _lastSampleTick;
    private DateTime _lastSampleUtc;
    private string? _candidateKey;
    private TimeSpan _candidateSince;
    private bool _idleSampleWritten;
    private bool _foregroundFaultReported;
    private bool _urlFaultReported;
    private bool _sessionEnded;
    private volatile bool _running;

    public ActivityTrackingService(
        IActivitySampleRepository samples,
        IStudySessionRepository sessions,
        IBlockRuleRepository rules,
        IForegroundActivityPort foreground,
        IBrowserUrlProviderPort urls,
        IMonotonicClockPort clock,
        IAppLogger logger,
        TimeProvider timeProvider,
        ActivityTrackingOptions options)
    {
        _samples = samples ?? throw new ArgumentNullException(nameof(samples));
        _sessions = sessions ?? throw new ArgumentNullException(nameof(sessions));
        _rules = rules ?? throw new ArgumentNullException(nameof(rules));
        _foreground = foreground ?? throw new ArgumentNullException(nameof(foreground));
        _urls = urls ?? throw new ArgumentNullException(nameof(urls));
        _clock = clock ?? throw new ArgumentNullException(nameof(clock));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _timeProvider = timeProvider ?? throw new ArgumentNullException(nameof(timeProvider));

        if (options is null)
            throw new ArgumentNullException(nameof(options));

        if (options.PollInterval < TimeSpan.FromMilliseconds(10) ||
            options.PollInterval > TimeSpan.FromMinutes(1))
        {
            throw new ArgumentOutOfRangeException(
                nameof(options),
                "PollInterval must be between 10 ms and 1 minute.");
        }

        if (options.SampleInterval < TimeSpan.FromSeconds(1) ||
            options.SampleInterval > TimeSpan.FromMinutes(5))
        {
            throw new ArgumentOutOfRangeException(
                nameof(options),
                "SampleInterval must be between 1 second and 5 minutes.");
        }

        if (options.ForegroundDebounce < TimeSpan.Zero ||
            options.ForegroundDebounce > TimeSpan.FromMinutes(1))
        {
            throw new ArgumentOutOfRangeException(
                nameof(options),
                "ForegroundDebounce must be between 0 and 1 minute.");
        }

        if (options.IdleThreshold < options.ForegroundDebounce ||
            options.IdleThreshold > TimeSpan.FromDays(1))
        {
            throw new ArgumentOutOfRangeException(
                nameof(options),
                "IdleThreshold must be at least the debounce and at most 1 day.");
        }

        if (options.MaxSampleGap < options.SampleInterval ||
            options.MaxSampleGap > TimeSpan.FromDays(1))
        {
            throw new ArgumentOutOfRangeException(
                nameof(options),
                "MaxSampleGap must be at least the sample interval and at most 1 day.");
        }

        _options = options;
    }

    public bool IsRunning => _running;

    public async Task StartAsync(
        StudySessionId sessionId,
        CancellationToken cancellationToken = default)
    {
        if (sessionId.IsEmpty)
            throw new ArgumentException("A session id is required.", nameof(sessionId));

        var rules = await LoadRulesAsync(cancellationToken).ConfigureAwait(false);

        await _lifecycleGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await StopCoreAsync().ConfigureAwait(false);

            var now = UtcNow();

            _sessionId = sessionId;
            _ruleSnapshot = rules;
            _lastSampleTick = _clock.GetTickCount();
            _lastSampleUtc = now;
            _candidateKey = null;
            _candidateSince = _lastSampleTick;
            _idleSampleWritten = false;
            _foregroundFaultReported = false;
            _urlFaultReported = false;
            _sessionEnded = false;
            _running = true;

            var cts = new CancellationTokenSource();
            _loopCts = cts;
            _loop = Task.Run(() => LoopAsync(cts.Token), CancellationToken.None);

            _logger.Info(LogCategories.Session, "Activity sampling started.");
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

    public async Task<bool> SampleOnceAsync(CancellationToken cancellationToken = default)
    {
        await _sampleGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            return await SampleCoreAsync(cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _sampleGate.Release();
        }
    }

    public async ValueTask DisposeAsync()
    {
        await StopAsync(CancellationToken.None).ConfigureAwait(false);
        _lifecycleGate.Dispose();
        _sampleGate.Dispose();
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
                _logger.Error(LogCategories.Session, "The activity loop stopped unexpectedly.", ex);
            }
        }

        cts?.Dispose();

        _sessionId = null;
        _candidateKey = null;
        _idleSampleWritten = false;
        _sessionEnded = false;
        _running = false;
    }

    private async Task LoopAsync(CancellationToken cancellationToken)
    {
        try
        {
            using var timer = new PeriodicTimer(_options.PollInterval);

            while (await timer.WaitForNextTickAsync(cancellationToken).ConfigureAwait(false))
            {
                try
                {
                    await SampleOnceAsync(cancellationToken).ConfigureAwait(false);
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                    break;
                }
                catch (Exception ex)
                {
                    _logger.Error(LogCategories.Session, "Activity sampling beat faulted.", ex);
                }

                if (_sessionEnded)
                    break;
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
        catch (Exception ex)
        {
            _logger.Error(LogCategories.Session, "The activity loop stopped unexpectedly.", ex);
        }
        finally
        {
            _running = false;
        }
    }

    private async Task<bool> SampleCoreAsync(CancellationToken cancellationToken)
    {
        var sessionId = _sessionId;
        if (sessionId is not { } id || _sessionEnded)
            return false;

        var tick = _clock.GetTickCount();
        var elapsed = tick - _lastSampleTick;
        var now = ClampNow(UtcNow());

        var snapshot = ReadForeground();

        var idleSeconds = Math.Max(snapshot?.IdleSeconds ?? 0, 0);
        var isIdle = idleSeconds >= _options.IdleThreshold.TotalSeconds;

        if (!isIdle)
            _idleSampleWritten = false;

        UpdateCandidate(tick, snapshot);

        if (elapsed < _options.SampleInterval)
            return false;

        if (isIdle)
        {
            if (_idleSampleWritten)
            {
                // Suppress the cadence entirely while the same idle period
                // lasts so a long absence cannot create duplicate rows.
                return false;
            }
        }
        else if (tick - _candidateSince < _options.ForegroundDebounce)
        {
            return false;
        }

        var duration = (int)Math.Min(
            Math.Floor(Math.Max(elapsed.TotalSeconds, 0)),
            Math.Floor(_options.MaxSampleGap.TotalSeconds));

        var idleForSample = Math.Min(idleSeconds, duration);
        var categorySeconds = isIdle ? 0 : duration - idleForSample;

        var url = isIdle ? null : ReadUrl(snapshot);
        var domain = UrlSanitizer.DeriveDomain(url);
        // An idle foreground window is not an active class: the student may have
        // left Meet/Teams open, so the overlay must not claim class time.
        var isOnlineClass = !isIdle && OnlineClassDetector.IsOnlineClass(
            domain,
            snapshot?.ProcessName,
            snapshot?.WindowTitle);
        var category = ActivityClassifier.Classify(
            _ruleSnapshot,
            domain,
            snapshot?.ProcessName,
            isOnlineClass,
            now);

        ActivitySample sample;
        try
        {
            sample = ActivitySample.Capture(
                id,
                now,
                duration,
                category,
                isForeground: snapshot is not null,
                isOnlineClass,
                idleForSample,
                snapshot?.AppName,
                snapshot?.ProcessName,
                snapshot?.WindowTitle,
                url,
                url is null ? UrlSource.None : UrlSource.UIA);
        }
        catch (ArgumentException ex)
        {
            _logger.Warning(
                LogCategories.Session,
                $"Could not build an activity sample ({ex.GetType().Name}).");
            _lastSampleTick = tick;
            _lastSampleUtc = now;
            return false;
        }

        bool counted;
        try
        {
            counted = await _sessions
                .AddActivitySecondsAsync(
                    id,
                    category,
                    categorySeconds,
                    idleForSample,
                    isOnlineClass ? categorySeconds : 0,
                    now,
                    cancellationToken)
                .ConfigureAwait(false);
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
            _lastSampleTick = tick;
            _lastSampleUtc = now;
            return false;
        }

        if (!counted)
        {
            _logger.Warning(LogCategories.Session, SessionEndedMessage);
            _sessionEnded = true;
            _sessionId = null;
            return false;
        }

        try
        {
            await _samples.AddAsync(sample, cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            // The counters already advanced; retrying the whole sample would
            // double-count that window, so only the row is lost.
            _logger.Error(LogCategories.Session, "Could not store an activity sample.", ex);
        }

        _lastSampleTick = tick;
        _lastSampleUtc = now;

        if (isIdle)
            _idleSampleWritten = true;

        return true;
    }

    private ForegroundActivitySnapshot? ReadForeground()
    {
        try
        {
            var snapshot = _foreground.GetForegroundActivity();
            _foregroundFaultReported = false;
            return snapshot;
        }
        catch (Exception ex)
        {
            if (!_foregroundFaultReported)
            {
                _logger.Warning(
                    LogCategories.Session,
                    $"Foreground activity is unavailable ({ex.GetType().Name}).");
                _foregroundFaultReported = true;
            }

            return null;
        }
    }

    private string? ReadUrl(ForegroundActivitySnapshot? snapshot)
    {
        if (snapshot is null)
            return null;

        try
        {
            var url = UrlSanitizer.Sanitize(_urls.TryGetActiveUrl(snapshot));
            _urlFaultReported = false;
            return url;
        }
        catch (Exception ex)
        {
            if (!_urlFaultReported)
            {
                _logger.Warning(
                    LogCategories.Session,
                    $"Browser URL detection is unavailable ({ex.GetType().Name}).");
                _urlFaultReported = true;
            }

            return null;
        }
    }

    private void UpdateCandidate(TimeSpan tick, ForegroundActivitySnapshot? snapshot)
    {
        var key = BuildCandidateKey(snapshot);

        if (string.Equals(key, _candidateKey, StringComparison.Ordinal))
            return;

        _candidateKey = key;
        _candidateSince = tick;
    }

    private static string BuildCandidateKey(ForegroundActivitySnapshot? snapshot)
    {
        if (snapshot is null)
            return "\u0000";

        return string.Concat(
            snapshot.ProcessId.ToString(System.Globalization.CultureInfo.InvariantCulture),
            "\u0001",
            snapshot.ProcessName ?? string.Empty,
            "\u0001",
            snapshot.WindowTitle ?? string.Empty);
    }

    private DateTime ClampNow(DateTime now) =>
        now < _lastSampleUtc ? _lastSampleUtc : now;

    private async Task<BlockRuleSet> LoadRulesAsync(CancellationToken cancellationToken)
    {
        try
        {
            var rules = await _rules.ListAsync(cancellationToken).ConfigureAwait(false);
            return BlockRuleSet.Create(rules);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.Warning(
                LogCategories.Session,
                $"Could not load block rules for activity classification ({ex.GetType().Name}).");
            return BlockRuleSet.Create([]);
        }
    }

    private DateTime UtcNow() => _timeProvider.GetUtcNow().UtcDateTime;
}
