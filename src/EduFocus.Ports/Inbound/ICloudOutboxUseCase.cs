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

    Task<CloudOutboxStatus> GetStatusAsync(CancellationToken cancellationToken = default);
}
