using System.Text.Json;
using EduFocus.Domain.Entities;
using EduFocus.Ports.DTOs;
using EduFocus.Ports.Inbound;
using EduFocus.Ports.Outbound.Cloud;
using EduFocus.Ports.Outbound.Persistence;
using EduFocus.Ports.Outbound.Services;

namespace EduFocus.UseCases.Cloud;

/// <summary>
/// Offline-first enqueue for aggregate cloud events (#87). Enqueue never talks
/// to the network; the background worker drains the queue once the link is
/// paired. The queue is bounded to 5,000 pending events or 7 days.
/// </summary>
public sealed class CloudOutboxService : ICloudOutboxUseCase
{
    private readonly ICloudOutboxRepository _outbox;
    private readonly ICloudOutboxNotifier _notifier;
    private readonly IAppLogger _logger;
    private readonly TimeProvider _timeProvider;

    public CloudOutboxService(
        ICloudOutboxRepository outbox,
        ICloudOutboxNotifier notifier,
        IAppLogger logger,
        TimeProvider timeProvider)
    {
        _outbox = outbox ?? throw new ArgumentNullException(nameof(outbox));
        _notifier = notifier ?? throw new ArgumentNullException(nameof(notifier));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _timeProvider = timeProvider ?? throw new ArgumentNullException(nameof(timeProvider));
    }

    public Task EnqueueAsync(
        string type,
        JsonElement payload,
        CancellationToken cancellationToken = default) =>
        EnqueueCoreAsync(type, payload, clientEventId: null, cancellationToken);

    public Task EnqueueAsync(
        string type,
        JsonElement payload,
        string? clientEventId,
        CancellationToken cancellationToken = default) =>
        EnqueueCoreAsync(type, payload, clientEventId, cancellationToken);

    private async Task EnqueueCoreAsync(
        string type,
        JsonElement payload,
        string? clientEventId,
        CancellationToken cancellationToken)
    {
        var normalizedType = type?.Trim();
        if (!CloudEventTypes.IsKnown(normalizedType))
            throw new ArgumentException("Cloud event type is not supported.", nameof(type));
        if (payload.ValueKind != JsonValueKind.Object)
            throw new ArgumentException("Cloud event payload must be a JSON object.", nameof(payload));

        var normalizedClientEventId = string.IsNullOrWhiteSpace(clientEventId)
            ? Guid.NewGuid().ToString("D")
            : clientEventId.Trim();

        if (clientEventId is not null
            && await _outbox.ExistsAsync(normalizedClientEventId, cancellationToken).ConfigureAwait(false))
        {
            return;
        }

        var now = _timeProvider.GetUtcNow().UtcDateTime;
        var item = CloudOutboxEvent.Create(
            normalizedClientEventId,
            normalizedType!,
            payload.GetRawText(),
            now);
        await _outbox.AddAsync(item, cancellationToken).ConfigureAwait(false);

        var removed = await _outbox
            .TrimAsync(now, CloudOutboxLimits.MaxPendingEvents, CloudOutboxLimits.MaxEventAge, cancellationToken)
            .ConfigureAwait(false);
        if (removed > 0)
        {
            _logger.Warning(
                LogCategories.Cloud,
                $"Cloud outbox removed {removed} oldest event(s) to stay within limits.");
        }

        _notifier.NotifyNewEvent();
    }

    public Task<CloudOutboxStatus> GetStatusAsync(CancellationToken cancellationToken = default) =>
        _outbox.GetStatusAsync(cancellationToken);
}
