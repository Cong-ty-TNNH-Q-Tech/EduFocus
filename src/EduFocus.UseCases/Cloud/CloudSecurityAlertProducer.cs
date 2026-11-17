using System.Text.Json;
using EduFocus.Domain.Enums;
using EduFocus.Ports.Inbound;
using EduFocus.Ports.Outbound.Cloud;
using EduFocus.Ports.Outbound.Persistence;
using EduFocus.Ports.Outbound.Services;

namespace EduFocus.UseCases.Cloud;

/// <summary>
/// Mirrors tamper markers (clock jumps) as security alerts (#88). Only audit
/// rows of type <see cref="AuditEventType.TamperDetected"/> written after the
/// link was paired are considered, and each row maps to the deterministic
/// client event id "audit-{auditId}", so a scan repeated after a restart is a
/// no-op. Audit descriptions and metadata never leave the device.
/// </summary>
public sealed class CloudSecurityAlertProducer
{
    /// <summary>Shorter than the outbox retention so trimmed rows are never rescanned.</summary>
    internal static readonly TimeSpan ScanWindow = TimeSpan.FromDays(6);

    internal const int ScanLimit = 50;

    private readonly IAuditLogRepository _audits;
    private readonly ICloudLinkRepository _links;
    private readonly ICloudOutboxRepository _outbox;
    private readonly ICloudEventSink _sink;
    private readonly IAppLogger _logger;
    private readonly TimeProvider _timeProvider;

    public CloudSecurityAlertProducer(
        IAuditLogRepository audits,
        ICloudLinkRepository links,
        ICloudOutboxRepository outbox,
        ICloudEventSink sink,
        IAppLogger logger,
        TimeProvider timeProvider)
    {
        _audits = audits ?? throw new ArgumentNullException(nameof(audits));
        _links = links ?? throw new ArgumentNullException(nameof(links));
        _outbox = outbox ?? throw new ArgumentNullException(nameof(outbox));
        _sink = sink ?? throw new ArgumentNullException(nameof(sink));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _timeProvider = timeProvider ?? throw new ArgumentNullException(nameof(timeProvider));
    }

    /// <summary>Queues alerts for unseen tamper audits; returns how many were queued.</summary>
    public async Task<int> TryScanAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            var link = await _links.GetAsync(cancellationToken).ConfigureAwait(false);
            if (link is not { IsLinked: true } || link.LinkedAtUtc is not { } linkedAtUtc)
                return 0;

            var nowUtc = _timeProvider.GetUtcNow().UtcDateTime;
            var fromUtc = nowUtc - ScanWindow;
            if (linkedAtUtc > fromUtc)
                fromUtc = linkedAtUtc;

            var audits = await _audits
                .ListByTypeSinceAsync(AuditEventType.TamperDetected, fromUtc, ScanLimit, cancellationToken)
                .ConfigureAwait(false);

            var queued = 0;
            foreach (var audit in audits)
            {
                var clientEventId = $"audit-{audit.Id.Value:D}";
                if (await _outbox.ExistsAsync(clientEventId, cancellationToken).ConfigureAwait(false))
                    continue;

                var occurredAtUtc = new DateTimeOffset(
                    DateTime.SpecifyKind(audit.OccurredAtUtc, DateTimeKind.Utc));
                var localDate = DateOnly.FromDateTime(
                    TimeZoneInfo.ConvertTime(occurredAtUtc, _timeProvider.LocalTimeZone).DateTime);

                var payload = JsonSerializer.SerializeToElement(new
                {
                    kind = "clock_jump",
                    localDate,
                });

                if (!await _sink
                        .TryEnqueueAsync(CloudEventTypes.SecurityAlert, payload, clientEventId, cancellationToken)
                        .ConfigureAwait(false))
                {
                    break;
                }

                queued++;
            }

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
                $"Could not scan tamper audits for the cloud ({ex.GetType().Name}).");
            return 0;
        }
    }
}
