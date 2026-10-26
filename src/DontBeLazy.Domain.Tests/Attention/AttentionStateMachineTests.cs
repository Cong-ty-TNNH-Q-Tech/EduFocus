using DontBeLazy.Domain.Attention;
using FluentAssertions;
using Xunit;

namespace DontBeLazy.Domain.Tests.Attention;

public class AttentionStateMachineTests
{
    private static readonly AttentionThresholds Thresholds = new();
    private static readonly DateTimeOffset Start = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);

    private static FaceObservation Frontal => new(
        true, true, 1, 0.9f, 200, 150, 200, 200,
        new Landmark(270, 220), new Landmark(330, 220), new Landmark(300, 260),
        new Landmark(275, 300), new Landmark(325, 300),
        2, 0.10, 0.05, true);

    private static AttentionClassification Focused => Classify(Frontal, 120);
    private static AttentionClassification Turned => Classify(Frontal with { HeadYawRatio = 0.8 }, 120);
    private static AttentionClassification Absent => Classify(FaceObservation.NoFace, 120);
    private static AttentionClassification Dark => Classify(FaceObservation.NoFace, 10);

    private static AttentionClassification Classify(FaceObservation observation, double luminance) =>
        AttentionClassifier.Classify(observation, luminance, Thresholds);

    [Fact]
    public void Update_BeforeAnyCommit_LeavesStateUnknown()
    {
        var machine = new AttentionStateMachine(Thresholds);

        machine.Update(Turned, Start);

        machine.CurrentState.Should().BeNull();
    }

    [Fact]
    public void Update_Focused_CommitsImmediately()
    {
        var machine = new AttentionStateMachine(Thresholds);

        machine.Update(Focused, Start);

        machine.CurrentState.Should().Be(AttentionState.Focused);
        machine.CurrentStateSince.Should().Be(Start);
    }

    [Fact]
    public void Update_DistractedForLessThanThreshold_StaysFocused()
    {
        var machine = new AttentionStateMachine(Thresholds);
        machine.Update(Focused, Start);

        machine.Update(Turned, Start.AddSeconds(1));
        machine.Update(Turned, Start.AddSeconds(19));

        machine.CurrentState.Should().Be(AttentionState.Focused);
    }

    [Fact]
    public void Update_DistractedForThreshold_CommitsDistracted()
    {
        var machine = new AttentionStateMachine(Thresholds);
        machine.Update(Focused, Start);

        machine.Update(Turned, Start.AddSeconds(1));
        machine.Update(Turned, Start.AddSeconds(21));

        machine.CurrentState.Should().Be(AttentionState.Distracted);
    }

    [Fact]
    public void Update_FocusReturns_CommitsImmediately()
    {
        var machine = new AttentionStateMachine(Thresholds);
        machine.Update(Focused, Start);
        machine.Update(Turned, Start.AddSeconds(1));
        machine.Update(Turned, Start.AddSeconds(21));

        machine.Update(Focused, Start.AddSeconds(22));

        machine.CurrentState.Should().Be(AttentionState.Focused);
    }

    [Fact]
    public void Update_AbsentForThreshold_CommitsAbsent()
    {
        var machine = new AttentionStateMachine(Thresholds);
        machine.Update(Focused, Start);

        machine.Update(Absent, Start.AddSeconds(1));
        machine.Update(Absent, Start.AddSeconds(29));
        machine.CurrentState.Should().Be(AttentionState.Focused);

        machine.Update(Absent, Start.AddSeconds(31));
        machine.CurrentState.Should().Be(AttentionState.Absent);
    }

    [Fact]
    public void Update_CameraUnavailable_CommitsImmediately()
    {
        var machine = new AttentionStateMachine(Thresholds);

        machine.Update(Classify(FaceObservation.CameraUnavailable, 0), Start);

        machine.CurrentState.Should().Be(AttentionState.CameraUnavailable);
    }

    [Fact]
    public void Update_LowLightForThreshold_CommitsCameraUnavailable()
    {
        var machine = new AttentionStateMachine(Thresholds);

        machine.Update(Dark, Start);
        machine.Update(Dark, Start.AddSeconds(29));
        machine.CurrentState.Should().BeNull();

        machine.Update(Dark, Start.AddSeconds(31));
        machine.CurrentState.Should().Be(AttentionState.CameraUnavailable);
    }

    [Fact]
    public void Update_CandidateChanges_ResetsDebounce()
    {
        var machine = new AttentionStateMachine(Thresholds);
        machine.Update(Focused, Start);

        machine.Update(Turned, Start.AddSeconds(1));
        machine.Update(Absent, Start.AddSeconds(10));
        machine.Update(Turned, Start.AddSeconds(20));

        machine.CurrentState.Should().Be(AttentionState.Focused);

        machine.Update(Turned, Start.AddSeconds(40));
        machine.CurrentState.Should().Be(AttentionState.Distracted);
    }
}
