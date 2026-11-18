using System.Text.Json;
using EduFocus.Domain.Entities;
using EduFocus.Domain.Enums;
using EduFocus.Ports.Outbound.Cloud;
using EduFocus.Ports.Outbound.Persistence;
using EduFocus.Ports.Outbound.Services;

namespace EduFocus.Infrastructure.Cloud;

/// <summary>
/// Drains the durable cloud outbox (issue #87). The worker polls every 60
/// seconds, wakes immediately when an event is enqueued, sends at most 50
/// events per batch and never lets a cloud failure reach Protection or the UI.
/// A rejected token pauses sending until the link is paired again.
/// </summary>
public sealed class CloudSyncWorker : ICloudOutboxNotifier, IDisposable
{
    public const string RePairMessage = "Cần ghép nối lại";

    public static readonly TimeSpan PollInterval = TimeSpan.FromSeconds(60);
    public static readonly TimeSpan MaxBackoff = TimeSpan.FromMinutes(60);

    private static readonly TimeSpan DefaultRateLimitDelay = TimeSpan.FromMinutes(1);

    private readonly ICloudLinkRepository _links;
    private readonly ICloudOutboxRepository _outbox;
    private readonly ICloudSyncPort _sync;
    private readonly IAuditLogRepository _auditLogs;
    private readonly ICorrelationContext _correlation;
    private readonly IAppLogger _logger;
    private readonly TimeProvider _timeProvider;

    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly SemaphoreSlim _signal = new(0, 1);
    private readonly object _lifecycleLock = new();

    private CancellationTokenSource? _cts;
    private DateTime? _authFailedAtUtc;
    private bool _failureAudited;

    public CloudSyncWorker(
        ICloudLinkRepository links,
        ICloudOutboxRepository outbox,
        ICloudSyncPort sync,
        IAuditLogRepository auditLogs,
        ICorrelationContext correlation,
        IAppLogger logger,
        TimeProvider timeProvider)
    {
        _links = links ?? throw new ArgumentNullException(nameof(links));
        _outbox = outbox ?? throw new ArgumentNullException(nameof(outbox));
        _sync = sync ?? throw new ArgumentNullException(nameof(sync));
        _auditLogs = auditLogs ?? throw new ArgumentNullException(nameof(auditLogs));
        _correlation = correlation ?? throw new ArgumentNullException(nameof(correlation));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _timeProvider = timeProvider ?? throw new ArgumentNullException(nameof(timeProvider));
    }

    public void Start()
    {
        lock (_lifecycleLock)
        {
            if (_cts is not null)
                return;

            _cts = new CancellationTokenSource();
            var token = _cts.Token;
            _ = Task.Run(() => RunLoopAsync(token), CancellationToken.None);
        }
    }

    public void NotifyNewEvent()
    {
        try
        {
            _signal.Release();
        }
        catch (SemaphoreFullException)
        {
            // A pending wake-up already exists; the loop coalesces signals.
        }
    }

    /// <summary>One pass over the due queue. Callers may invoke it directly in tests.</summary>
    public async Task RunOnceAsync(CancellationToken cancellationToken = default)
    {
        if (!await _gate.WaitAsync(0, cancellationToken).ConfigureAwait(false))
            return;

        try
        {
            // Trim before the link check so a disabled or unlinked queue is
            // still bounded and years-old rows never linger indefinitely.
            var trimmed = await _outbox
                .TrimAsync(UtcNow(), CloudOutboxLimits.MaxPendingEvents, CloudOutboxLimits.MaxEventAge, cancellationToken)
                .ConfigureAwait(false);
            if (trimmed > 0)
            {
                _logger.Warning(
                    LogCategories.Cloud,
                    $"Cloud outbox removed {trimmed} oldest event(s) to stay within limits.");
            }

            var link = await _links.GetAsync(cancellationToken).ConfigureAwait(false);
            if (link is null
                || !link.IsEnabled
                || !link.IsLinked
                || string.IsNullOrEmpty(link.DesktopToken))
            {
                return;
            }

            if (_authFailedAtUtc is { } pausedAt && link.UpdatedAtUtc <= pausedAt)
                return;

            if (!Uri.TryCreate(link.CloudBaseUrl, UriKind.Absolute, out var baseUrl)
                || (baseUrl.Scheme != Uri.UriSchemeHttps && baseUrl.Scheme != Uri.UriSchemeHttp))
            {
                await RecordLinkErrorAsync(link, "Địa chỉ cloud không hợp lệ.", cancellationToken)
                    .ConfigureAwait(false);
                return;
            }

            await DrainAsync(link, baseUrl, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _gate.Release();
        }
    }

    public void Dispose()
    {
        CancellationTokenSource? cts;
        lock (_lifecycleLock)
        {
            cts = _cts;
            _cts = null;
        }

        if (cts is null)
            return;

        // Exiting never waits for a final flush.
        cts.Cancel();
        NotifyNewEvent();
    }

    private async Task RunLoopAsync(CancellationToken token)
    {
        while (!token.IsCancellationRequested)
        {
            try
            {
                await RunOnceAsync(token).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (token.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                TryLogError("Cloud sync worker pass failed.", ex);
            }

            try
            {
                await _signal.WaitAsync(PollInterval, token).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (ObjectDisposedException)
            {
                break;
            }
        }
    }

    private async Task DrainAsync(CloudLink link, Uri baseUrl, CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            var batch = await _outbox
                .GetDueAsync(UtcNow(), CloudOutboxLimits.BatchSize, cancellationToken)
                .ConfigureAwait(false);
            if (batch.Count == 0)
                return;

            try
            {
                var events = batch.Select(ToSyncEvent).ToList();
                var result = await _sync
                    .SendEventsAsync(baseUrl, link.DesktopToken!, events, cancellationToken)
                    .ConfigureAwait(false);

                await ApplyResultsAsync(batch, result, cancellationToken).ConfigureAwait(false);
                var syncedAt = UtcNow(link);
                var recorded = await _links
                    .TryRecordSyncAsync(link.DesktopToken!, syncedAt, syncedAt, cancellationToken)
                    .ConfigureAwait(false);
                if (!recorded)
                    return;

                _authFailedAtUtc = null;
                _failureAudited = false;
            }
            catch (CloudSyncException ex)
            {
                await HandleFailureAsync(link, batch, ex, cancellationToken).ConfigureAwait(false);
                return;
            }

            if (batch.Count < CloudOutboxLimits.BatchSize)
                return;
        }
    }

    private async Task ApplyResultsAsync(
        IReadOnlyList<CloudOutboxEvent> batch,
        CloudSyncBatchResult result,
        CancellationToken cancellationToken)
    {
        var now = UtcNow();
        var byId = result.Results
            .GroupBy(item => item.ClientEventId, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.Last(), StringComparer.OrdinalIgnoreCase);

        var sent = new List<Guid>();
        var missing = new List<CloudOutboxEvent>();

        foreach (var item in batch)
        {
            if (!byId.TryGetValue(item.ClientEventId, out var eventResult))
            {
                missing.Add(item);
                continue;
            }

            switch (eventResult.Outcome)
            {
                case CloudEventOutcome.Applied:
                case CloudEventOutcome.Duplicate:
                    sent.Add(item.Id);
                    break;

                case CloudEventOutcome.Rejected:
                    var reason = string.IsNullOrWhiteSpace(eventResult.Code)
                        ? eventResult.Message
                        : eventResult.Code;
                    await _outbox
                        .MarkFailedAsync([item.Id], now, reason, cancellationToken)
                        .ConfigureAwait(false);
                    _logger.Warning(
                        LogCategories.Cloud,
                        $"Cloud event rejected (type={item.Type}, code={reason ?? "rejected"}).");
                    break;
            }
        }

        if (sent.Count > 0)
            await _outbox.MarkSentAsync(sent, now, cancellationToken).ConfigureAwait(false);

        if (missing.Count > 0)
        {
            _logger.Warning(
                LogCategories.Cloud,
                $"Cloud sync response omitted {missing.Count} event(s); they stay queued for retry.");
            await ScheduleRetryAsync(missing, "missing_result", now, cancellationToken).ConfigureAwait(false);
        }
    }

    private async Task HandleFailureAsync(
        CloudLink link,
        IReadOnlyList<CloudOutboxEvent> batch,
        CloudSyncException exception,
        CancellationToken cancellationToken)
    {
        var now = UtcNow(link);

        if (exception.Code == CloudErrorCodes.Unauthorized || exception.StatusCode is 401 or 403)
        {
            await _links
                .TryRecordErrorAsync(link.DesktopToken!, RePairMessage, now, cancellationToken)
                .ConfigureAwait(false);
            _authFailedAtUtc = now;

            await AuditFailureOnceAsync(exception.Code, batch.Count, now, cancellationToken)
                .ConfigureAwait(false);
            _logger.Warning(
                LogCategories.Cloud,
                "Cloud sync stopped: the device token was rejected. Outbox kept; pairing required.");
            return;
        }

        if (exception.Code == CloudErrorCodes.RateLimited || exception.StatusCode == 429)
        {
            var delay = exception.RetryAfter is { } retryAfter && retryAfter > TimeSpan.Zero
                ? retryAfter
                : DefaultRateLimitDelay;
            await _outbox
                .ScheduleRetryAsync(
                    batch.Select(item => item.Id).ToList(),
                    now + delay,
                    CloudErrorCodes.RateLimited,
                    cancellationToken)
                .ConfigureAwait(false);

            await _links
                .TryRecordErrorAsync(
                    link.DesktopToken!,
                    "Máy chủ cloud đang giới hạn tần suất. Sẽ tự thử lại.",
                    now,
                    cancellationToken)
                .ConfigureAwait(false);
            _logger.Warning(
                LogCategories.Cloud,
                $"Cloud sync rate limited; retrying after {delay.TotalSeconds:0} second(s).");
            return;
        }

        await ScheduleRetryAsync(batch, exception.Code, now, cancellationToken).ConfigureAwait(false);
        await _links
            .TryRecordErrorAsync(link.DesktopToken!, exception.Message, now, cancellationToken)
            .ConfigureAwait(false);
        await AuditFailureOnceAsync(exception.Code, batch.Count, now, cancellationToken)
            .ConfigureAwait(false);
        _logger.Warning(
            LogCategories.Cloud,
            $"Cloud sync failed ({exception.Code}); {batch.Count} event(s) kept for retry.");
    }

    private Task ScheduleRetryAsync(
        IReadOnlyList<CloudOutboxEvent> items,
        string? reason,
        DateTime now,
        CancellationToken cancellationToken)
    {
        var retries = items
            .GroupBy(item => BackoffFor(item.AttemptCount))
            .Select(group => _outbox.ScheduleRetryAsync(
                group.Select(item => item.Id).ToList(),
                now + group.Key,
                reason,
                cancellationToken))
            .ToList();

        return Task.WhenAll(retries);
    }

    private async Task AuditFailureOnceAsync(
        string code,
        int pending,
        DateTime now,
        CancellationToken cancellationToken)
    {
        if (_failureAudited)
            return;

        _failureAudited = true;

        try
        {
            var metadata = JsonSerializer.Serialize(new { code, pending });
            await _auditLogs
                .AddAsync(
                    AuditLog.Record(
                        AuditActor.System,
                        AuditEventType.CloudSyncFailed,
                        "Cloud sync failed; events stay queued for retry.",
                        now,
                        metadata,
                        _correlation.Current),
                    cancellationToken)
                .ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            TryLogError("Cloud sync failure audit could not be written.", ex);
        }
    }

    private async Task RecordLinkErrorAsync(CloudLink link, string message, CancellationToken cancellationToken)
    {
        await _links
            .TryRecordErrorAsync(link.DesktopToken!, message, UtcNow(link), cancellationToken)
            .ConfigureAwait(false);
    }

    private static CloudSyncEvent ToSyncEvent(CloudOutboxEvent item)
    {
        using var document = JsonDocument.Parse(item.PayloadJson);
        return new CloudSyncEvent(
            item.ClientEventId,
            item.Type,
            new DateTimeOffset(DateTime.SpecifyKind(item.CreatedAtUtc, DateTimeKind.Utc)),
            document.RootElement.Clone());
    }

    private static TimeSpan BackoffFor(int attemptCount)
    {
        var minutes = Math.Min(MaxBackoff.TotalMinutes, Math.Pow(2, Math.Max(0, attemptCount)));
        return TimeSpan.FromMinutes(minutes);
    }

    private DateTime UtcNow() => _timeProvider.GetUtcNow().UtcDateTime;

    private DateTime UtcNow(CloudLink link)
    {
        var now = UtcNow();
        return now < link.UpdatedAtUtc ? link.UpdatedAtUtc : now;
    }

    private void TryLogError(string message, Exception ex)
    {
        try
        {
            _logger.Error(LogCategories.Cloud, message, ex);
        }
        catch
        {
            // Logging must never take the worker down.
        }
    }
}
