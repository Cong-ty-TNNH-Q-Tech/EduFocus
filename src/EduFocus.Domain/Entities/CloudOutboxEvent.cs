using EduFocus.Domain.Common;
using EduFocus.Domain.Privacy;

namespace EduFocus.Domain.Entities;

/// <summary>
/// A queued cloud sync event. The client event id is generated once at enqueue
/// time so retries stay idempotent on the server; the payload holds aggregate
/// data only and is never written to the technical log.
/// </summary>
public sealed class CloudOutboxEvent
{
    public const int MaxTypeLength = 32;
    public const int MaxPayloadLength = 16_384;
    public const int MaxErrorLength = 512;

    private CloudOutboxEvent(
        Guid id,
        Guid clientEventId,
        string type,
        string payloadJson,
        DateTime createdAtUtc,
        DateTime nextAttemptAtUtc,
        int attemptCount,
        DateTime? sentAtUtc,
        DateTime? failedAtUtc,
        string? lastError)
    {
        Id = id;
        ClientEventId = clientEventId;
        Type = type;
        PayloadJson = payloadJson;
        CreatedAtUtc = createdAtUtc;
        NextAttemptAtUtc = nextAttemptAtUtc;
        AttemptCount = attemptCount;
        SentAtUtc = sentAtUtc;
        FailedAtUtc = failedAtUtc;
        LastError = lastError;
    }

    public Guid Id { get; }

    public Guid ClientEventId { get; }

    public string Type { get; }

    public string PayloadJson { get; }

    public DateTime CreatedAtUtc { get; }

    public DateTime NextAttemptAtUtc { get; private set; }

    public int AttemptCount { get; private set; }

    public DateTime? SentAtUtc { get; private set; }

    /// <summary>Set when the server rejected the event; rejected events are never retried.</summary>
    public DateTime? FailedAtUtc { get; private set; }

    public string? LastError { get; private set; }

    public bool IsPending => SentAtUtc is null && FailedAtUtc is null;

    public static CloudOutboxEvent Create(
        Guid clientEventId,
        string type,
        string payloadJson,
        DateTime nowUtc)
    {
        Guard.Utc(nowUtc, nameof(nowUtc));
        if (clientEventId == Guid.Empty)
            throw new ArgumentException("Client event id is required.", nameof(clientEventId));
        Guard.NotNullOrWhiteSpace(type, nameof(type));
        Guard.NotNullOrWhiteSpace(payloadJson, nameof(payloadJson));

        var normalizedType = type.Trim();
        if (normalizedType.Length > MaxTypeLength || normalizedType.Any(char.IsControl))
            throw new ArgumentException($"Type must be at most {MaxTypeLength} characters.", nameof(type));

        if (payloadJson.Length > MaxPayloadLength)
            throw new ArgumentException($"Payload must be at most {MaxPayloadLength} characters.", nameof(payloadJson));

        return new CloudOutboxEvent(
            Guid.NewGuid(),
            clientEventId,
            normalizedType,
            payloadJson,
            nowUtc,
            nowUtc,
            attemptCount: 0,
            sentAtUtc: null,
            failedAtUtc: null,
            lastError: null);
    }

    public void MarkSent(DateTime sentAtUtc)
    {
        Guard.Utc(sentAtUtc, nameof(sentAtUtc));
        ThrowIfTerminal();

        SentAtUtc = sentAtUtc;
        LastError = null;
    }

    public void ScheduleRetry(DateTime nextAttemptAtUtc, string? error)
    {
        Guard.Utc(nextAttemptAtUtc, nameof(nextAttemptAtUtc));
        ThrowIfTerminal();

        AttemptCount++;
        NextAttemptAtUtc = nextAttemptAtUtc;
        LastError = TextSanitizer.Clean(error, MaxErrorLength);
    }

    public void MarkFailed(DateTime failedAtUtc, string? reason)
    {
        Guard.Utc(failedAtUtc, nameof(failedAtUtc));
        ThrowIfTerminal();

        FailedAtUtc = failedAtUtc;
        LastError = TextSanitizer.Clean(reason, MaxErrorLength);
    }

    private void ThrowIfTerminal()
    {
        if (SentAtUtc is not null || FailedAtUtc is not null)
            throw new InvalidOperationException("Cloud outbox event is already terminal.");
    }
}
