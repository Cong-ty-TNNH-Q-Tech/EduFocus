namespace EduFocus.Domain.Attention;

public sealed class AttentionStateMachine
{
    private readonly AttentionThresholds _thresholds;
    private RawAttentionState _candidateState = RawAttentionState.Unknown;
    private DateTimeOffset _candidateSince;
    private bool _hasCandidate;

    public AttentionStateMachine(AttentionThresholds thresholds)
    {
        _thresholds = thresholds;
    }

    public AttentionState? CurrentState { get; private set; }
    public DateTimeOffset? CurrentStateSince { get; private set; }
    public RawAttentionState LastRawState { get; private set; } = RawAttentionState.Unknown;
    public string LastReason { get; private set; } = string.Empty;

    public AttentionState? Update(AttentionClassification classification, DateTimeOffset now)
    {
        LastRawState = classification.State;
        LastReason = classification.Reason;

        if (classification.State == RawAttentionState.CameraUnavailable)
        {
            Commit(AttentionState.CameraUnavailable, now);
            _hasCandidate = false;
            return CurrentState;
        }

        var target = Map(classification.State);
        if (target is null)
        {
            _hasCandidate = false;
            return CurrentState;
        }

        if (target == CurrentState)
        {
            _hasCandidate = false;
            return CurrentState;
        }

        if (!_hasCandidate || _candidateState != classification.State)
        {
            _candidateState = classification.State;
            _candidateSince = now;
            _hasCandidate = true;
        }

        if ((now - _candidateSince).TotalSeconds >= RequiredSeconds(classification.State))
        {
            Commit(target.Value, now);
            _hasCandidate = false;
        }

        return CurrentState;
    }

    private void Commit(AttentionState state, DateTimeOffset now)
    {
        if (state == CurrentState)
            return;

        CurrentState = state;
        CurrentStateSince = now;
    }

    private double RequiredSeconds(RawAttentionState state) => state switch
    {
        RawAttentionState.Focused => _thresholds.FocusedAfterSeconds,
        RawAttentionState.Distracted => _thresholds.DistractedAfterSeconds,
        RawAttentionState.Absent => _thresholds.AbsentAfterSeconds,
        RawAttentionState.Drowsy => _thresholds.DrowsyAfterSeconds,
        RawAttentionState.LowLight => _thresholds.LowLightAfterSeconds,
        _ => 0
    };

    private static AttentionState? Map(RawAttentionState state) => state switch
    {
        RawAttentionState.Focused => AttentionState.Focused,
        RawAttentionState.Distracted => AttentionState.Distracted,
        RawAttentionState.Absent => AttentionState.Absent,
        RawAttentionState.Drowsy => AttentionState.Drowsy,
        RawAttentionState.LowLight => AttentionState.CameraUnavailable,
        RawAttentionState.CameraUnavailable => AttentionState.CameraUnavailable,
        _ => null
    };
}
