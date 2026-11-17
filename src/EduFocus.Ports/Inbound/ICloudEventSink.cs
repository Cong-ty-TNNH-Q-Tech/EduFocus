using System.Text.Json;

namespace EduFocus.Ports.Inbound;

/// <summary>
/// Best-effort enqueue used by aggregate event producers. The sink is a no-op
/// while the cloud link is absent, disabled or not paired, and it never throws
/// into the caller: protection, attention and schedule flows must keep working
/// with the cloud off.
/// </summary>
public interface ICloudEventSink
{
    /// <summary>
    /// Queues one aggregate event when the link is paired. Returns false when
    /// the event was not queued (cloud off or a storage failure).
    /// <paramref name="clientEventId"/> makes producers idempotent across
    /// restarts; null lets the sink generate a fresh id.
    /// </summary>
    Task<bool> TryEnqueueAsync(
        string type,
        JsonElement payload,
        string? clientEventId = null,
        CancellationToken cancellationToken = default);
}
