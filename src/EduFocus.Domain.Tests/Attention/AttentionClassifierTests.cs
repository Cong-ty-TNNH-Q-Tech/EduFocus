using EduFocus.Domain.Attention;
using FluentAssertions;
using Xunit;

namespace EduFocus.Domain.Tests.Attention;

public class AttentionClassifierTests
{
    private static readonly AttentionThresholds Thresholds = new();

    private static FaceObservation Frontal => new(
        CameraAvailable: true,
        FacePresent: true,
        FaceCount: 1,
        Confidence: 0.92f,
        X: 200,
        Y: 150,
        Width: 200,
        Height: 200,
        RightEye: new Landmark(270, 220),
        LeftEye: new Landmark(330, 220),
        Nose: new Landmark(300, 260),
        RightMouth: new Landmark(275, 300),
        LeftMouth: new Landmark(325, 300),
        EyeCount: 2,
        HeadYawRatio: 0.10,
        HeadPitchRatio: 0.05,
        LandmarksAvailable: true);

    [Fact]
    public void Classify_FrontalFace_ReturnsFocused()
    {
        var result = AttentionClassifier.Classify(Frontal, luminance: 120, Thresholds);

        result.State.Should().Be(RawAttentionState.Focused);
        result.Reason.Should().Be("frontal");
        result.Confidence.Should().Be(0.92f);
    }

    [Fact]
    public void Classify_TurnedFace_ReturnsDistracted()
    {
        var turned = Frontal with { HeadYawRatio = 0.80 };

        var result = AttentionClassifier.Classify(turned, luminance: 120, Thresholds);

        result.State.Should().Be(RawAttentionState.Distracted);
        result.Reason.Should().Be("off-screen");
    }

    [Fact]
    public void Classify_NoFace_ReturnsAbsent()
    {
        var result = AttentionClassifier.Classify(FaceObservation.NoFace, luminance: 120, Thresholds);

        result.State.Should().Be(RawAttentionState.Absent);
        result.Reason.Should().Be("no-face");
    }

    [Fact]
    public void Classify_NoFaceInLowLight_ReturnsLowLight()
    {
        var result = AttentionClassifier.Classify(FaceObservation.NoFace, luminance: 10, Thresholds);

        result.State.Should().Be(RawAttentionState.LowLight);
        result.Reason.Should().Be("no-face-low-light");
    }

    [Fact]
    public void Classify_CameraUnavailable_ReturnsCameraUnavailable()
    {
        var result = AttentionClassifier.Classify(FaceObservation.CameraUnavailable, luminance: 0, Thresholds);

        result.State.Should().Be(RawAttentionState.CameraUnavailable);
    }

    [Fact]
    public void Classify_EyesClosed_ReturnsDrowsy()
    {
        var drowsy = Frontal with { EyesClosed = true };

        var result = AttentionClassifier.Classify(drowsy, luminance: 120, Thresholds);

        result.State.Should().Be(RawAttentionState.Drowsy);
        result.Reason.Should().Be("eyes-closed");
        result.Confidence.Should().Be(0.92f);
    }

    [Fact]
    public void Classify_EyesOpen_ReturnsFocused()
    {
        var result = AttentionClassifier.Classify(Frontal with { EyesClosed = false }, 120, Thresholds);

        result.State.Should().Be(RawAttentionState.Focused);
    }

    [Fact]
    public void Classify_UnknownEyeState_DoesNotGuessDrowsy()
    {
        var result = AttentionClassifier.Classify(Frontal with { EyesClosed = null }, 120, Thresholds);

        result.State.Should().Be(RawAttentionState.Focused);
        result.Reason.Should().Be("frontal");
    }

    [Fact]
    public void Classify_NoFace_IgnoresEyeState()
    {
        var result = AttentionClassifier.Classify(
            FaceObservation.NoFace with { EyesClosed = true },
            luminance: 120,
            Thresholds);

        result.State.Should().Be(RawAttentionState.Absent);
    }

    [Fact]
    public void Classify_NoLandmarks_UsesWiderCenteringThreshold()
    {
        var centered = Frontal with
        {
            LandmarksAvailable = false,
            HeadYawRatio = 0.40,
            HeadPitchRatio = 0.40
        };
        var offCenter = centered with { HeadYawRatio = 0.50 };

        AttentionClassifier.Classify(centered, 120, Thresholds).State
            .Should().Be(RawAttentionState.Focused);
        AttentionClassifier.Classify(offCenter, 120, Thresholds).State
            .Should().Be(RawAttentionState.Distracted);
    }
}
