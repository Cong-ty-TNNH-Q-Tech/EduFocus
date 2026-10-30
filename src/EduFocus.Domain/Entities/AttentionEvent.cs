using EduFocus.Domain.Attention;
using EduFocus.Domain.Common;
using EduFocus.Domain.Privacy;
using EduFocus.Domain.ValueObjects;

namespace EduFocus.Domain.Entities;

/// <summary>
/// An attention state interval. Events open only when the state changes and are
/// closed exactly once; no frames or landmarks are stored (BR-13).
/// </summary>
public sealed class AttentionEvent
{
    public const int MaxAppNameLength = 128;
    public const int MaxNoteLength = 256;

    private AttentionEvent(
        AttentionEventId id,
        StudySessionId sessionId,
        AttentionState state,
        DateTime startedAtUtc,
        double? confidence,
        string? appName,
        DomainName? domain,
        string? note,
        DateTime createdAtUtc)
    {
        Id = id;
        SessionId = sessionId;
        State = state;
        StartedAtUtc = startedAtUtc;
        Confidence = confidence;
        AppName = appName;
        Domain = domain;
        Note = note;
        CreatedAtUtc = createdAtUtc;
    }

    public AttentionEventId Id { get; }

    public StudySessionId SessionId { get; }

    public AttentionState State { get; }

    public DateTime StartedAtUtc { get; }

    public DateTime? EndedAtUtc { get; private set; }

    public int DurationSeconds { get; private set; }

    public double? Confidence { get; }

    public string? AppName { get; }

    public DomainName? Domain { get; }

    public string? Note { get; }

    public DateTime CreatedAtUtc { get; }

    public bool IsOpen => EndedAtUtc is null;

    public static AttentionEvent Open(
        StudySessionId sessionId,
        AttentionState state,
        DateTime startedAtUtc,
        double? confidence = null,
        string? appName = null,
        DomainName? domain = null,
        string? note = null,
        DateTime? createdAtUtc = null)
    {
        Guard.NotDefault(sessionId, nameof(sessionId));
        Guard.Defined(state, nameof(state));
        Guard.Utc(startedAtUtc, nameof(startedAtUtc));
        Guard.MaxLength(note, MaxNoteLength, nameof(note));

        if (confidence is < 0 or > 1)
            throw new ArgumentOutOfRangeException(nameof(confidence), confidence, "Confidence must be between 0 and 1.");

        var created = createdAtUtc ?? startedAtUtc;
        Guard.Utc(created, nameof(createdAtUtc));

        if (created < startedAtUtc)
            throw new ArgumentException("Creation time must not precede the event start.", nameof(createdAtUtc));

        return new AttentionEvent(
            AttentionEventId.New(),
            sessionId,
            state,
            startedAtUtc,
            confidence,
            TextSanitizer.Clean(appName, MaxAppNameLength),
            domain,
            TextSanitizer.Clean(note, MaxNoteLength),
            created);
    }

    public void Close(DateTime endedAtUtc)
    {
        if (!IsOpen)
            throw new InvalidOperationException("Attention event is already closed.");

        Guard.Utc(endedAtUtc, nameof(endedAtUtc));

        if (endedAtUtc < StartedAtUtc)
            throw new ArgumentException("End time must not precede the event start.", nameof(endedAtUtc));

        DurationSeconds = (int)Math.Round(
            (endedAtUtc - StartedAtUtc).TotalSeconds,
            MidpointRounding.AwayFromZero);

        EndedAtUtc = endedAtUtc;
    }
}
