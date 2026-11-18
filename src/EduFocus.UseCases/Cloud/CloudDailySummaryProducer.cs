using System.Text.Json;
using EduFocus.Domain.Enums;
using EduFocus.Domain.Reports;
using EduFocus.Ports.Inbound;
using EduFocus.Ports.Outbound.Cloud;
using EduFocus.Ports.Outbound.Persistence;
using EduFocus.Ports.Outbound.Services;

namespace EduFocus.UseCases.Cloud;

/// <summary>
/// Captures the day aggregate for the parent app (#88). The payload mirrors
/// the sync contract and is built only from <see cref="DailyStudySummary"/>;
/// raw samples, events, app names and URLs never reach the cloud. Each capture
/// gets a fresh client event id and the relay upserts by local date, so the
/// newest capture wins.
/// </summary>
public sealed class CloudDailySummaryProducer
{
    private readonly IStudySessionRepository _sessions;
    private readonly ICloudEventSink _sink;
    private readonly IAppLogger _logger;
    private readonly TimeProvider _timeProvider;
    private DateOnly? _lastLocalDate;

    public CloudDailySummaryProducer(
        IStudySessionRepository sessions,
        ICloudEventSink sink,
        IAppLogger logger,
        TimeProvider timeProvider)
    {
        _sessions = sessions ?? throw new ArgumentNullException(nameof(sessions));
        _sink = sink ?? throw new ArgumentNullException(nameof(sink));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _timeProvider = timeProvider ?? throw new ArgumentNullException(nameof(timeProvider));
    }

    /// <summary>
    /// Captures the current local day (and the previous local day when a
    /// midnight rollover is observed); returns false when nothing was queued.
    /// </summary>
    public async Task<bool> TryPublishAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            var now = _timeProvider.GetUtcNow();
            var localDate = DateOnly.FromDateTime(_timeProvider.GetLocalNow().DateTime);
            var timeZoneId = CloudTimeZones.ToSendableId(_timeProvider.LocalTimeZone);

            var anyQueued = false;
            if (_lastLocalDate is { } previousDate && previousDate < localDate)
            {
                anyQueued |= await TryPublishDateAsync(previousDate, timeZoneId, now, cancellationToken)
                    .ConfigureAwait(false);
            }

            _lastLocalDate = localDate;
            anyQueued |= await TryPublishDateAsync(localDate, timeZoneId, now, cancellationToken)
                .ConfigureAwait(false);

            return anyQueued;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.Warning(
                LogCategories.Cloud,
                $"Could not capture the daily summary ({ex.GetType().Name}).");
            return false;
        }
    }

    private async Task<bool> TryPublishDateAsync(
        DateOnly localDate,
        string? timeZoneId,
        DateTimeOffset generatedAtUtc,
        CancellationToken cancellationToken)
    {
        var summary = await _sessions
            .GetDailySummaryAsync(localDate, cancellationToken)
            .ConfigureAwait(false);
        if (summary is null || !summary.HasSessions)
            return false;

        var payload = BuildPayload(summary, timeZoneId, generatedAtUtc);

        return await _sink
            .TryEnqueueAsync(CloudEventTypes.DailySummary, payload, clientEventId: null, cancellationToken)
            .ConfigureAwait(false);
    }

    private static JsonElement BuildPayload(
        DailyStudySummary summary,
        string? timeZoneId,
        DateTimeOffset generatedAtUtc)
    {
        var attention = summary.Attention;

        return JsonSerializer.SerializeToElement(new
        {
            localDate = summary.LocalDate,
            timeZoneId,
            generatedAtUtc,
            sessionsCount = summary.SessionCount,
            activeSeconds = summary.ActiveSeconds,
            studySeconds = summary.StudySeconds,
            idleSeconds = summary.IdleSeconds,
            onlineClassSeconds = summary.OnlineClassSeconds,
            monitoringEligibleSeconds = summary.MonitoringEligibleSeconds,
            monitoredSeconds = attention.MonitoredSeconds,
            focusedSeconds = summary.FocusedSeconds,
            distractedSeconds = summary.DistractedSeconds,
            absentSeconds = summary.AbsentSeconds,
            drowsySeconds = summary.DrowsySeconds,
            blockedAttempts = summary.BlockedAttemptCount,
            attentionPercent = attention.AttentionPercent,
            coveragePercent = attention.CoveragePercent,
        });
    }
}
