using System.Text.Json;
using EduFocus.Ports.DTOs;

namespace EduFocus.Ports.Inbound;

/// <summary>
/// Offline-first ingress for aggregate cloud events. Producers enqueue here;
/// the background worker drains the queue when the link is paired.
/// </summary>
public interface ICloudOutboxUseCase
{
    Task EnqueueAsync(
        string type,
        JsonElement payload,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Enqueues with a caller-chosen client event id. Repeating the same id is
    /// a no-op, which lets producers (security alerts) stay exactly-once
    /// across restarts.
    /// </summary>
    Task EnqueueAsync(
        string type,
        JsonElement payload,
        string? clientEventId,
        CancellationToken cancellationToken = default);

    Task<CloudOutboxStatus> GetStatusAsync(CancellationToken cancellationToken = default);
}
