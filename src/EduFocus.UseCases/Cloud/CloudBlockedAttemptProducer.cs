using System.Text.Json;
using EduFocus.Ports.Inbound;
using EduFocus.Ports.Outbound.Cloud;
using EduFocus.Ports.Outbound.Persistence;
using EduFocus.Ports.Outbound.Services;

namespace EduFocus.UseCases.Cloud;

/// <summary>
/// Watches the local blocked-attempt log for web blocks and raises one generic
/// parent alert per <see cref="ThrottleWindow"/> (#88 follow-up). Only the local
/// date leaves the device; domains, URLs and window titles never reach the
/// cloud. The first poll only establishes the cursor, so alerts are never
/// backfilled from attempts that happened before this process started.
/// </summary>
public sealed class CloudBlockedAttemptProducer
{
    public static readonly TimeSpan ThrottleWindow = TimeSpan.FromMinutes(15);

    private readonly IBlockedAttemptRepository _attempts;
    private readonly ICloudEventSink _sink;
    private readonly IAppLogger _logger;
    private readonly TimeProvider _timeProvider;

    private DateTime? _cursorUtc;
    private DateTimeOffset? _lastQueuedAtUtc;

    public CloudBlockedAttemptProducer(
        IBlockedAttemptRepository attempts,
        ICloudEventSink sink,
        IAppLogger logger,
        TimeProvider timeProvider)
    {
        _attempts = attempts ?? throw new ArgumentNullException(nameof(attempts));
        _sink = sink ?? throw new ArgumentNullException(nameof(sink));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _timeProvider = timeProvider ?? throw new ArgumentNullException(nameof(timeProvider));
    }

    public async Task<bool> TryPublishAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            var now = _timeProvider.GetUtcNow();

            if (_lastQueuedAtUtc is { } lastQueuedAt && lastQueuedAt > now)
                _lastQueuedAtUtc = null;

            if (_cursorUtc is { } cursor && cursor > now.UtcDateTime)
                _cursorUtc = now.UtcDateTime;

            if (_cursorUtc is null)
            {
                _cursorUtc = now.UtcDateTime;
                return false;
            }

            var latest = await _attempts
                .GetLatestWebAttemptCreatedAtUtcAsync(_cursorUtc.Value, cancellationToken)
                .ConfigureAwait(false);
            if (latest is null)
                return false;

            _cursorUtc = latest;

            if (_lastQueuedAtUtc is { } lastQueued && now - lastQueued < ThrottleWindow)
                return false;

            var attemptAt = new DateTimeOffset(DateTime.SpecifyKind(latest.Value, DateTimeKind.Utc));
            var localDate = DateOnly.FromDateTime(
                TimeZoneInfo.ConvertTime(attemptAt, _timeProvider.LocalTimeZone).DateTime);
            var payload = JsonSerializer.SerializeToElement(new { localDate });

            var queued = await _sink
                .TryEnqueueAsync(CloudEventTypes.BlockedAttempt, payload, clientEventId: null, cancellationToken)
                .ConfigureAwait(false);
            if (queued)
                _lastQueuedAtUtc = now;

            return queued;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.Warning(
                LogCategories.Cloud,
                $"Could not queue a blocked-attempt alert ({ex.GetType().Name}).");
            return false;
        }
    }
}
