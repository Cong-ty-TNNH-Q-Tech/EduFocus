using EduFocus.Domain.Attention;
using EduFocus.Domain.Common;
using EduFocus.Domain.Enums;
using EduFocus.Domain.ValueObjects;

namespace EduFocus.Domain.Entities;

/// <summary>
/// Study session aggregate. Counters only accrue while the session is active and
/// percentages are derived from monitored time, never from wall-clock duration
/// (BR-11, BR-15, BR-17). The "only one active session" rule is enforced by a
/// unique partial index in the database.
/// </summary>
public sealed class StudySession
{
    public const int MaxNoteLength = 512;

    private StudySession(
        StudySessionId id,
        DateTime startedAtUtc,
        DateOnly startLocalDate,
        SessionTrigger trigger,
        bool monitoringEnabled,
        DateTime createdAtUtc)
    {
        Id = id;
        StartedAtUtc = startedAtUtc;
        LastHeartbeatAtUtc = startedAtUtc;
        StartLocalDate = startLocalDate;
        Status = StudySessionStatus.Active;
        Trigger = trigger;
        MonitoringEnabled = monitoringEnabled;
        CreatedAtUtc = createdAtUtc;
        UpdatedAtUtc = createdAtUtc;
    }

    public StudySessionId Id { get; }

    public DateTime StartedAtUtc { get; }

    public DateTime? EndedAtUtc { get; private set; }

    public DateTime LastHeartbeatAtUtc { get; private set; }

    public DateOnly StartLocalDate { get; }

    public StudySessionStatus Status { get; private set; }

    public SessionTrigger Trigger { get; }

    public bool MonitoringEnabled { get; }

    public int ActiveSeconds { get; private set; }

    public int FocusedSeconds { get; private set; }

    public int DistractedSeconds { get; private set; }

    public int AbsentSeconds { get; private set; }

    public int DrowsySeconds { get; private set; }

    /// <summary>Camera-unavailable time is recorded separately and is not monitored time.</summary>
    public int CameraUnavailableSeconds { get; private set; }

    public int IdleSeconds { get; private set; }

    public int StudySeconds { get; private set; }

    public int DistractionSeconds { get; private set; }

    public int NeutralSeconds { get; private set; }

    /// <summary>Overlay; not added to study/distraction/neutral totals.</summary>
    public int OnlineClassSeconds { get; private set; }

    public int MonitoredSeconds => FocusedSeconds + DistractedSeconds + AbsentSeconds + DrowsySeconds;

    public int MonitoringEligibleSeconds { get; private set; }

    public double? AttentionPercent =>
        MonitoredSeconds > 0 ? FocusedSeconds * 100.0 / MonitoredSeconds : null;

    public double? CoveragePercent =>
        MonitoringEligibleSeconds > 0 ? MonitoredSeconds * 100.0 / MonitoringEligibleSeconds : null;

    public double? ConfidenceAvg { get; private set; }

    public int BlockedAttemptCount { get; private set; }

    public string? Note { get; private set; }

    public DateTime CreatedAtUtc { get; }

    public DateTime UpdatedAtUtc { get; private set; }

    public bool IsActive => Status == StudySessionStatus.Active;

    public static StudySession Start(
        DateTime startedAtUtc,
        DateOnly startLocalDate,
        SessionTrigger trigger,
        bool monitoringEnabled)
    {
        Guard.Utc(startedAtUtc, nameof(startedAtUtc));
        Guard.Defined(trigger, nameof(trigger));

        if (startLocalDate == default)
            throw new ArgumentException("Local date is required.", nameof(startLocalDate));

        return new StudySession(
            StudySessionId.New(),
            startedAtUtc,
            startLocalDate,
            trigger,
            monitoringEnabled,
            startedAtUtc);
    }

    public void Heartbeat(DateTime nowUtc)
    {
        EnsureActive();
        Guard.Utc(nowUtc, nameof(nowUtc));
        EnsureNotBeforeHeartbeat(nowUtc, nameof(nowUtc));

        LastHeartbeatAtUtc = nowUtc;
        Touch(nowUtc);
    }

    public void AddActiveSeconds(int seconds, DateTime nowUtc)
    {
        EnsureActive();
        Guard.NonNegative(seconds, nameof(seconds));
        ActiveSeconds += seconds;
        Touch(nowUtc);
    }

    public void AddAttentionSeconds(AttentionState state, int seconds, DateTime nowUtc)
    {
        EnsureActive();
        Guard.Defined(state, nameof(state));
        Guard.NonNegative(seconds, nameof(seconds));

        switch (state)
        {
            case AttentionState.Focused:
                FocusedSeconds += seconds;
                break;
            case AttentionState.Distracted:
                DistractedSeconds += seconds;
                break;
            case AttentionState.Absent:
                AbsentSeconds += seconds;
                break;
            case AttentionState.Drowsy:
                DrowsySeconds += seconds;
                break;
            case AttentionState.CameraUnavailable:
                CameraUnavailableSeconds += seconds;
                break;
        }

        Touch(nowUtc);
    }

    public void AddMonitoringEligibleSeconds(int seconds, DateTime nowUtc)
    {
        EnsureActive();
        Guard.NonNegative(seconds, nameof(seconds));
        MonitoringEligibleSeconds += seconds;
        Touch(nowUtc);
    }

    public void AddCategorySeconds(ActivityCategory category, int seconds, DateTime nowUtc)
    {
        EnsureActive();
        Guard.Defined(category, nameof(category));
        Guard.NonNegative(seconds, nameof(seconds));

        switch (category)
        {
            case ActivityCategory.Study:
                StudySeconds += seconds;
                break;
            case ActivityCategory.Distraction:
                DistractionSeconds += seconds;
                break;
            default:
                NeutralSeconds += seconds;
                break;
        }

        Touch(nowUtc);
    }

    public void AddOnlineClassSeconds(int seconds, DateTime nowUtc)
    {
        EnsureActive();
        Guard.NonNegative(seconds, nameof(seconds));
        OnlineClassSeconds += seconds;
        Touch(nowUtc);
    }

    public void AddIdleSeconds(int seconds, DateTime nowUtc)
    {
        EnsureActive();
        Guard.NonNegative(seconds, nameof(seconds));
        IdleSeconds += seconds;
        Touch(nowUtc);
    }

    public void IncrementBlockedAttempts(DateTime nowUtc, int count = 1)
    {
        EnsureActive();
        Guard.NonNegative(count, nameof(count));
        BlockedAttemptCount += count;
        Touch(nowUtc);
    }

    public void SetAttentionConfidence(double? confidence, DateTime nowUtc)
    {
        EnsureActive();

        if (confidence is < 0 or > 1)
            throw new ArgumentOutOfRangeException(nameof(confidence), confidence, "Confidence must be between 0 and 1.");

        ConfidenceAvg = confidence;
        Touch(nowUtc);
    }

    public void SetNote(string? note, DateTime nowUtc)
    {
        EnsureActive();
        Guard.MaxLength(note, MaxNoteLength, nameof(note));
        Note = Privacy.TextSanitizer.Clean(note, MaxNoteLength);
        Touch(nowUtc);
    }

    public void Complete(DateTime endedAtUtc) => End(endedAtUtc, StudySessionStatus.Completed);

    public void Abort(DateTime endedAtUtc) => End(endedAtUtc, StudySessionStatus.Aborted);

    /// <summary>Closes an interrupted session at its last heartbeat; downtime is never counted.</summary>
    public void Interrupt()
    {
        EnsureActive();

        Status = StudySessionStatus.Interrupted;
        EndedAtUtc = LastHeartbeatAtUtc;

        if (LastHeartbeatAtUtc > UpdatedAtUtc)
            UpdatedAtUtc = LastHeartbeatAtUtc;
    }

    private void End(DateTime endedAtUtc, StudySessionStatus status)
    {
        EnsureActive();
        Guard.Utc(endedAtUtc, nameof(endedAtUtc));
        EnsureNotBeforeHeartbeat(endedAtUtc, nameof(endedAtUtc));

        EndedAtUtc = endedAtUtc;
        LastHeartbeatAtUtc = endedAtUtc;
        Status = status;
        Touch(endedAtUtc);
    }

    private void EnsureActive()
    {
        if (!IsActive)
            throw new InvalidOperationException("Study session is no longer active.");
    }

    private void EnsureNotBeforeHeartbeat(DateTime timestampUtc, string parameterName)
    {
        if (timestampUtc < LastHeartbeatAtUtc)
            throw new ArgumentException("Timestamp must not precede the last heartbeat.", parameterName);
    }

    private void Touch(DateTime nowUtc)
    {
        Guard.Utc(nowUtc, nameof(nowUtc));

        if (nowUtc < UpdatedAtUtc)
            throw new ArgumentException("Timestamp must not move backwards.", nameof(nowUtc));

        UpdatedAtUtc = nowUtc;
    }
}
