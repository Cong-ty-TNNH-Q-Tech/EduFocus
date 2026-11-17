using System.Text.Json;
using EduFocus.Ports.Inbound;

namespace EduFocus.UseCases.Tests.TestSupport;

internal sealed class RecordingCloudEventSink : ICloudEventSink
{
    public List<EnqueuedEvent> Enqueued { get; } = [];

    public bool Result { get; set; } = true;

    public bool Throw { get; set; }

    public int Attempts { get; private set; }

    public Task<bool> TryEnqueueAsync(
        string type,
        JsonElement payload,
        string? clientEventId = null,
        CancellationToken cancellationToken = default)
    {
        Attempts++;

        if (Throw)
            throw new InvalidOperationException("The event sink is unavailable.");

        if (!Result)
            return Task.FromResult(false);

        Enqueued.Add(new EnqueuedEvent(type, payload.Clone(), clientEventId));
        return Task.FromResult(true);
    }

    public sealed record EnqueuedEvent(string Type, JsonElement Payload, string? ClientEventId);
}
