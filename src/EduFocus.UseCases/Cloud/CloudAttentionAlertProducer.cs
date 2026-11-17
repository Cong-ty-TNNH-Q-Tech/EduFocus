using System.Text.Json;
using EduFocus.Domain.Attention;
using EduFocus.Ports.Inbound;
using EduFocus.Ports.Outbound.Attention;
using EduFocus.Ports.Outbound.Cloud;
using EduFocus.Ports.Outbound.Services;

namespace EduFocus.UseCases.Cloud;

/// <summary>
/// Turns sustained Distracted/Absent streaks into parent alerts (#88). The
/// first alert needs five continuous minutes, later updates are throttled to
/// one per fifteen minutes, and any state change or monitoring pause restarts
/// the streak. Only the state and its duration are used; frames and images are
/// never touched, and Focused, Drowsy, CameraUnavailable and inference errors
/// never alert.
/// </summary>
public sealed class CloudAttentionAlertProducer : IAttentionStateObserver
{
    public static readonly TimeSpan MinimumAlertDuration = TimeSpan.FromMinutes(5);
    public static readonly TimeSpan UpdateInterval = TimeSpan.FromMinutes(15);

    private readonly ICloudEventSink _sink;
    private readonly IAppLogger _logger;
    private readonly TimeProvider _timeProvider;
    private readonly object _gate = new();

    private AttentionState? _currentState;
    private DateTimeOffset _stateStartedAtUtc;
    private DateTimeOffset? _lastAlertAtUtc;

    public CloudAttentionAlertProducer(
        ICloudEventSink sink,
        IAppLogger logger,
        TimeProvider timeProvider)
    {
        _sink = sink ?? throw new ArgumentNullException(nameof(sink));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _timeProvider = timeProvider ?? throw new ArgumentNullException(nameof(timeProvider));
    }

    public async ValueTask OnStateObservedAsync(
        AttentionState? state,
        DateTimeOffset observedAtUtc,
        CancellationToken cancellationToken = default)
    {
        Alert? alert;
        lock (_gate)
        {
            if (state is null)
            {
                ResetCore();
                return;
            }

            if (state != _currentState)
            {
                _currentState = state;
                _stateStartedAtUtc = observedAtUtc;
                _lastAlertAtUtc = null;
                return;
            }

            if (state is not (AttentionState.Distracted or AttentionState.Absent))
                return;

            var duration = observedAtUtc - _stateStartedAtUtc;
            if (duration < MinimumAlertDuration)
                return;
            if (_lastAlertAtUtc is { } last && observedAtUtc - last < UpdateInterval)
                return;

            _lastAlertAtUtc = observedAtUtc;
            alert = new Alert(state.Value, _stateStartedAtUtc, (int)duration.TotalSeconds);
        }

        try
        {
            var localDate = DateOnly.FromDateTime(
                TimeZoneInfo.ConvertTime(alert.StateStartedAtUtc, _timeProvider.LocalTimeZone).DateTime);

            var payload = JsonSerializer.SerializeToElement(new
            {
                kind = alert.State == AttentionState.Distracted ? "distracted" : "absent",
                localDate,
                startedAtUtc = alert.StateStartedAtUtc,
                durationSeconds = alert.DurationSeconds,
            });

            await _sink
                .TryEnqueueAsync(CloudEventTypes.AttentionAlert, payload, clientEventId: null, cancellationToken)
                .ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.Warning(
                LogCategories.Cloud,
                $"Could not queue an attention alert ({ex.GetType().Name}).");
        }
    }

    private void ResetCore()
    {
        _currentState = null;
        _stateStartedAtUtc = default;
        _lastAlertAtUtc = null;
    }

    private sealed record Alert(AttentionState State, DateTimeOffset StateStartedAtUtc, int DurationSeconds);
}
