using System.Threading.Channels;
using EduFocus.Domain.Attention;
using EduFocus.Domain.Entities;
using EduFocus.Domain.ValueObjects;
using EduFocus.Ports.DTOs;
using EduFocus.Ports.Inbound;
using EduFocus.Ports.Outbound.Persistence;
using EduFocus.Ports.Outbound.Services;

namespace EduFocus.UseCases.Attention;

/// <summary>
/// Turns attention samples into stable states and event intervals (UC08,
/// issue #32). Samples are serialized through a channel so classification,
/// event changes and counter updates never race with each other. Events open
/// only when the smoothed state changes; camera failures are recorded
/// immediately and never enter the attention denominator.
/// </summary>
public sealed class AttentionMonitorService : IAttentionMonitorUseCase, IAsyncDisposable
{
    private const string WriteWarning =
        "Could not persist attention time; the next sample will retry.";

    private const string SessionEndedMessage =
        "Attention monitoring stopped because the study session is no longer active.";

    private readonly IAttentionEventRepository _events;
    private readonly IStudySessionRepository _sessions;
    private readonly IAttentionPipelineUseCase _pipeline;
    private readonly IAppLogger _logger;
    private readonly TimeProvider _timeProvider;
    private readonly AttentionMonitoringOptions _options;
    private readonly AttentionThresholds _thresholds;
    private readonly SemaphoreSlim _lifecycleGate = new(1, 1);

    private Channel<WorkItem>? _queue;
    private Task? _writer;
    private StudySessionId? _sessionId;
    private AttentionStateMachine? _machine;
    private AttentionStateWindow? _window;
    private AttentionEvent? _openEvent;
    private AttentionState? _persistedState;
    private DateTimeOffset? _lastSampleAt;
    private double _pendingSeconds;
    private bool _writeFaultReported;
    private volatile bool _running;
    private int _sessionEnded;

    public AttentionMonitorService(
        IAttentionEventRepository events,
        IStudySessionRepository sessions,
        IAttentionPipelineUseCase pipeline,
        IAppLogger logger,
        TimeProvider timeProvider,
        AttentionMonitoringOptions options,
        AttentionThresholds thresholds)
    {
        _events = events ?? throw new ArgumentNullException(nameof(events));
        _sessions = sessions ?? throw new ArgumentNullException(nameof(sessions));
        _pipeline = pipeline ?? throw new ArgumentNullException(nameof(pipeline));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _timeProvider = timeProvider ?? throw new ArgumentNullException(nameof(timeProvider));
        _thresholds = thresholds ?? throw new ArgumentNullException(nameof(thresholds));

        if (options is null)
            throw new ArgumentNullException(nameof(options));

        if (options.WindowDuration < TimeSpan.FromSeconds(5) ||
            options.WindowDuration > TimeSpan.FromSeconds(10))
        {
            throw new ArgumentOutOfRangeException(
                nameof(options),
                "WindowDuration must be between 5 and 10 seconds.");
        }

        if (options.MinimumWindowSamples < 2 ||
            options.MinimumWindowSamples > options.WindowDuration.TotalSeconds * 4)
        {
            throw new ArgumentOutOfRangeException(
                nameof(options),
                "MinimumWindowSamples must be at least 2 and fit inside the window.");
        }

        if (options.MajorityShare is < 0.5 or > 1)
        {
            throw new ArgumentOutOfRangeException(
                nameof(options),
                "MajorityShare must be between 0.5 and 1.");
        }

        if (options.MaxSampleGap < TimeSpan.FromSeconds(1) ||
            options.MaxSampleGap > TimeSpan.FromMinutes(1))
        {
            throw new ArgumentOutOfRangeException(
                nameof(options),
                "MaxSampleGap must be between 1 second and 1 minute.");
        }

        _options = options;
    }

    public bool IsRunning => _running;

    public AttentionState? CurrentState => _machine?.CurrentState;

    public async Task StartAsync(
        StudySessionId sessionId,
        CancellationToken cancellationToken = default)
    {
        if (sessionId.IsEmpty)
            throw new ArgumentException("A session id is required.", nameof(sessionId));

        await _lifecycleGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await StopCoreAsync().ConfigureAwait(false);

            _sessionId = sessionId;
            _machine = new AttentionStateMachine(_thresholds);
            _window = new AttentionStateWindow(
                _options.WindowDuration,
                _options.MinimumWindowSamples,
                _options.MajorityShare);
            _openEvent = null;
            _persistedState = null;
            _lastSampleAt = null;
            _pendingSeconds = 0;
            _writeFaultReported = false;
            Interlocked.Exchange(ref _sessionEnded, 0);

            var queue = Channel.CreateUnbounded<WorkItem>(new UnboundedChannelOptions
            {
                SingleReader = true,
                SingleWriter = false
            });

            _queue = queue;
            _writer = Task.Run(() => WriteLoopAsync(queue.Reader), CancellationToken.None);
            _running = true;
            _pipeline.SampleProduced += OnSampleProduced;

            _logger.Info(LogCategories.Attention, "Attention monitoring started.");
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

    public async Task<bool> ProcessAsync(
        AttentionSample sample,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(sample);

        var queue = _queue;
        if (queue is null || !_running || Volatile.Read(ref _sessionEnded) == 1)
            return false;

        var completion = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var item = new WorkItem(sample, completion);

        if (!queue.Writer.TryWrite(item))
            return false;

        return await completion.Task.WaitAsync(cancellationToken).ConfigureAwait(false);
    }

    public async ValueTask DisposeAsync()
    {
        await StopAsync(CancellationToken.None).ConfigureAwait(false);
        _lifecycleGate.Dispose();
    }

    private async Task StopCoreAsync()
    {
        var wasRunning = _running;
        _running = false;
        _pipeline.SampleProduced -= OnSampleProduced;

        var queue = _queue;
        var writer = _writer;
        _queue = null;
        _writer = null;

        var drained = true;
        if (queue is not null)
        {
            queue.Writer.TryComplete();

            if (writer is not null)
            {
                try
                {
                    await writer.WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(false);
                }
                catch (TimeoutException)
                {
                    drained = false;
                    _logger.Warning(LogCategories.Attention, "Attention writer did not stop in time.");
                }
                catch (Exception ex)
                {
                    _logger.Error(LogCategories.Attention, "The attention writer faulted.", ex);
                }
            }
        }

        if (drained)
            await CloseOpenEventAsync(_lastSampleAt ?? _timeProvider.GetUtcNow()).ConfigureAwait(false);

        _sessionId = null;
        _machine = null;
        _window = null;
        _openEvent = null;
        _persistedState = null;
        _lastSampleAt = null;
        _pendingSeconds = 0;
        _writeFaultReported = false;
        Interlocked.Exchange(ref _sessionEnded, 0);

        if (wasRunning)
            _logger.Info(LogCategories.Attention, "Attention monitoring stopped.");
    }

    private async Task WriteLoopAsync(ChannelReader<WorkItem> reader)
    {
        await foreach (var item in reader.ReadAllAsync().ConfigureAwait(false))
        {
            try
            {
                var result = await ProcessCoreAsync(item.Sample, CancellationToken.None)
                    .ConfigureAwait(false);
                item.Completion?.TrySetResult(result);
            }
            catch (OperationCanceledException)
            {
                item.Completion?.TrySetCanceled();
            }
            catch (Exception ex)
            {
                _logger.Error(LogCategories.Attention, "Attention sample processing failed.", ex);
                item.Completion?.TrySetException(ex);
            }
        }
    }

    private async Task<bool> ProcessCoreAsync(AttentionSample sample, CancellationToken cancellationToken)
    {
        var sessionId = _sessionId;
        var machine = _machine;
        var window = _window;

        if (sessionId is not { } id || machine is null || window is null || !_running)
            return false;

        var sampledAt = sample.SampledAtUtc;

        if (sample.CameraFailure != CameraFailureKind.None)
        {
            var camera = new AttentionClassification(
                RawAttentionState.CameraUnavailable,
                Confidence: 0,
                $"camera-{sample.CameraFailure}");

            return await ApplyClassificationAsync(id, machine, camera, sampledAt, cancellationToken)
                .ConfigureAwait(false);
        }

        if (sample.Observation is not { } observation)
        {
            // Inference failed: the camera is healthy but this tick is not
            // evidence of absence, so nothing is counted or transitioned.
            _lastSampleAt = sampledAt;
            _pendingSeconds = 0;
            return false;
        }

        var classification = AttentionClassifier.Classify(observation, sample.Luminance, _thresholds);
        var smoothed = window.Observe(classification, sampledAt);

        if (smoothed is null)
        {
            var current = machine.CurrentState;
            if (current is null)
            {
                _lastSampleAt = sampledAt;
                return false;
            }

            var seconds = TakeElapsedSeconds(sampledAt);
            var counted = await AddSecondsAsync(
                id,
                current.Value,
                seconds,
                ConfidenceFor(current.Value, classification),
                sampledAt,
                cancellationToken).ConfigureAwait(false);

            if (!counted)
                await HandleSessionEndedAsync(sampledAt).ConfigureAwait(false);

            return counted;
        }

        return await ApplyClassificationAsync(id, machine, smoothed, sampledAt, cancellationToken)
            .ConfigureAwait(false);
    }

    private async Task<bool> ApplyClassificationAsync(
        StudySessionId sessionId,
        AttentionStateMachine machine,
        AttentionClassification classification,
        DateTimeOffset sampledAt,
        CancellationToken cancellationToken)
    {
        machine.Update(classification, sampledAt);
        var state = machine.CurrentState;

        if (state is null)
        {
            _lastSampleAt = sampledAt;
            return false;
        }

        var seconds = TakeElapsedSeconds(sampledAt);
        var confidence = ConfidenceFor(state.Value, classification);

        var counted = await AddSecondsAsync(
            sessionId,
            state.Value,
            seconds,
            confidence,
            sampledAt,
            cancellationToken).ConfigureAwait(false);

        if (!counted)
        {
            await HandleSessionEndedAsync(sampledAt).ConfigureAwait(false);
            return false;
        }

        if (state != _persistedState)
            return await TryTransitionAsync(sessionId, state.Value, classification, sampledAt)
                .ConfigureAwait(false);

        return true;
    }

    private async Task<bool> AddSecondsAsync(
        StudySessionId sessionId,
        AttentionState state,
        int seconds,
        double? confidence,
        DateTimeOffset sampledAt,
        CancellationToken cancellationToken)
    {
        if (seconds <= 0)
            return true;

        try
        {
            var counted = await _sessions
                .AddAttentionSecondsAsync(
                    sessionId,
                    state,
                    seconds,
                    confidence,
                    sampledAt.UtcDateTime,
                    cancellationToken)
                .ConfigureAwait(false);

            _writeFaultReported = false;
            return counted;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            if (!_writeFaultReported)
            {
                _writeFaultReported = true;
                _logger.Warning(
                    LogCategories.Attention,
                    $"{WriteWarning} ({ex.GetType().Name})");
            }

            // The tick is lost rather than double counted; transitions still
            // proceed so the event timeline stays accurate.
            return true;
        }
    }

    private async Task<bool> TryTransitionAsync(
        StudySessionId sessionId,
        AttentionState state,
        AttentionClassification classification,
        DateTimeOffset sampledAt)
    {
        if (_openEvent is not null)
        {
            try
            {
                var closeAt = sampledAt.UtcDateTime < _openEvent.StartedAtUtc
                    ? _openEvent.StartedAtUtc
                    : sampledAt.UtcDateTime;

                _openEvent.Close(closeAt);
                await _events.UpdateAsync(_openEvent, CancellationToken.None).ConfigureAwait(false);
                _openEvent = null;
            }
            catch (Exception ex)
            {
                _logger.Error(LogCategories.Attention, "Could not close an attention event.", ex);
                return false;
            }
        }

        try
        {
            var attentionEvent = AttentionEvent.Open(
                sessionId,
                state,
                sampledAt.UtcDateTime,
                state == AttentionState.CameraUnavailable ? null : classification.Confidence,
                note: classification.Reason);

            await _events.AddAsync(attentionEvent, CancellationToken.None).ConfigureAwait(false);
            _openEvent = attentionEvent;
            _persistedState = state;
            return true;
        }
        catch (Exception ex)
        {
            _logger.Error(LogCategories.Attention, "Could not open an attention event.", ex);
            _persistedState = null;
            return false;
        }
    }

    private async Task CloseOpenEventAsync(DateTimeOffset endedAt)
    {
        var open = _openEvent;
        _openEvent = null;

        if (open is null)
            return;

        try
        {
            var closeAt = endedAt.UtcDateTime < open.StartedAtUtc
                ? open.StartedAtUtc
                : endedAt.UtcDateTime;

            open.Close(closeAt);
            await _events.UpdateAsync(open, CancellationToken.None).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.Error(LogCategories.Attention, "Could not close the open attention event.", ex);
        }
    }

    private async Task HandleSessionEndedAsync(DateTimeOffset sampledAt)
    {
        await CloseOpenEventAsync(sampledAt).ConfigureAwait(false);

        _running = false;
        Interlocked.Exchange(ref _sessionEnded, 1);
        _pipeline.SampleProduced -= OnSampleProduced;
        _queue?.Writer.TryComplete();

        _logger.Warning(LogCategories.Attention, SessionEndedMessage);
    }

    private int TakeElapsedSeconds(DateTimeOffset sampledAt)
    {
        if (_lastSampleAt is { } last)
        {
            var elapsed = sampledAt - last;

            if (elapsed > TimeSpan.Zero && elapsed <= _options.MaxSampleGap)
                _pendingSeconds += elapsed.TotalSeconds;
            else if (elapsed > _options.MaxSampleGap)
                _pendingSeconds = 0;
        }

        _lastSampleAt = sampledAt;

        var seconds = (int)Math.Floor(_pendingSeconds);
        if (seconds > 0)
            _pendingSeconds -= seconds;

        return seconds;
    }

    private static double? ConfidenceFor(AttentionState state, AttentionClassification classification) =>
        state == AttentionState.CameraUnavailable ? null : classification.Confidence;

    private void OnSampleProduced(object? sender, AttentionSample sample)
    {
        var queue = _queue;
        if (queue is null || !_running || Volatile.Read(ref _sessionEnded) == 1)
            return;

        queue.Writer.TryWrite(new WorkItem(sample, Completion: null));
    }

    private sealed record WorkItem(AttentionSample Sample, TaskCompletionSource<bool>? Completion);
}
