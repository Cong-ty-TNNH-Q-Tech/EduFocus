using EduFocus.Domain.Attention;
using FluentAssertions;

namespace EduFocus.Domain.Tests.Attention;

public class CameraHealthEvaluatorTests
{
    [Fact]
    public void A_bright_frame_with_a_face_is_ready()
    {
        var result = CameraHealthEvaluator.Evaluate(
            CameraHealthProblem.None,
            [new CameraCheckReading(120, true), new CameraCheckReading(90, true)],
            attemptedFrames: 2,
            new CameraCheckThresholds());

        result.Health.Should().Be(CameraHealth.Ready);
        result.Problem.Should().Be(CameraHealthProblem.None);
        result.IsReady.Should().BeTrue();
        result.AttemptedFrames.Should().Be(2);
        result.AnalyzedFrames.Should().Be(2);
        result.FaceFrames.Should().Be(2);
        result.MeanLuminance.Should().BeApproximately(105, 0.0001);
    }

    [Fact]
    public void Frames_below_the_low_light_threshold_are_reported_as_too_dark()
    {
        var result = CameraHealthEvaluator.Evaluate(
            CameraHealthProblem.None,
            [new CameraCheckReading(12, true), new CameraCheckReading(20, true), new CameraCheckReading(30, true)],
            attemptedFrames: 3,
            new CameraCheckThresholds());

        result.Health.Should().Be(CameraHealth.TooDark);
        result.IsReady.Should().BeFalse();
        result.MeanLuminance.Should().BeApproximately(20.6667, 0.001);
    }

    [Fact]
    public void Most_frames_dark_is_too_dark_even_when_the_mean_is_bright()
    {
        var result = CameraHealthEvaluator.Evaluate(
            CameraHealthProblem.None,
            [
                new CameraCheckReading(10, true),
                new CameraCheckReading(10, true),
                new CameraCheckReading(200, true),
                new CameraCheckReading(200, true)
            ],
            attemptedFrames: 4,
            new CameraCheckThresholds());

        result.Health.Should().Be(CameraHealth.TooDark);
    }

    [Fact]
    public void A_dark_face_in_a_bright_room_is_reported_as_too_dark()
    {
        var result = CameraHealthEvaluator.Evaluate(
            CameraHealthProblem.None,
            [
                new CameraCheckReading(180, true, FaceLuminance: 18),
                new CameraCheckReading(170, true, FaceLuminance: 24)
            ],
            attemptedFrames: 2,
            new CameraCheckThresholds());

        result.Health.Should().Be(CameraHealth.TooDark);
        result.FaceFrames.Should().Be(2);
    }

    [Fact]
    public void A_lit_face_in_a_dark_room_is_ready()
    {
        var result = CameraHealthEvaluator.Evaluate(
            CameraHealthProblem.None,
            [
                new CameraCheckReading(12, true, FaceLuminance: 120),
                new CameraCheckReading(15, true, FaceLuminance: 96)
            ],
            attemptedFrames: 2,
            new CameraCheckThresholds());

        result.Health.Should().Be(CameraHealth.Ready);
    }

    [Fact]
    public void The_face_median_ignores_a_single_bright_outlier()
    {
        var result = CameraHealthEvaluator.Evaluate(
            CameraHealthProblem.None,
            [
                new CameraCheckReading(150, true, FaceLuminance: 20),
                new CameraCheckReading(150, true, FaceLuminance: 25),
                new CameraCheckReading(150, true, FaceLuminance: 200)
            ],
            attemptedFrames: 3,
            new CameraCheckThresholds());

        result.Health.Should().Be(CameraHealth.TooDark);
    }

    [Fact]
    public void A_face_frame_without_face_luminance_falls_back_to_the_whole_frame()
    {
        var result = CameraHealthEvaluator.Evaluate(
            CameraHealthProblem.None,
            [new CameraCheckReading(150, true), new CameraCheckReading(140, true)],
            attemptedFrames: 2,
            new CameraCheckThresholds());

        result.Health.Should().Be(CameraHealth.Ready);
    }

    [Fact]
    public void A_custom_face_luminance_threshold_is_honoured()
    {
        var readings = new[] { new CameraCheckReading(150, true, FaceLuminance: 60) };

        var strict = CameraHealthEvaluator.Evaluate(
            CameraHealthProblem.None,
            readings,
            attemptedFrames: 1,
            new CameraCheckThresholds(FaceLuminanceThreshold: 80));

        strict.Health.Should().Be(CameraHealth.TooDark);
    }

    [Fact]
    public void No_analyzed_frames_means_the_camera_returned_no_image()
    {
        var result = CameraHealthEvaluator.Evaluate(
            CameraHealthProblem.None,
            [],
            attemptedFrames: 3,
            new CameraCheckThresholds());

        result.Health.Should().Be(CameraHealth.NoImage);
        result.AnalyzedFrames.Should().Be(0);
        result.MeanLuminance.Should().BeNull();
    }

    [Fact]
    public void A_bright_frame_without_a_face_is_reported_separately_from_darkness()
    {
        var result = CameraHealthEvaluator.Evaluate(
            CameraHealthProblem.None,
            [new CameraCheckReading(150, false), new CameraCheckReading(180, false)],
            attemptedFrames: 2,
            new CameraCheckThresholds());

        result.Health.Should().Be(CameraHealth.NoFace);
        result.FaceFrames.Should().Be(0);
    }

    [Fact]
    public void An_unavailable_camera_keeps_its_problem_instead_of_evaluating_frames()
    {
        var result = CameraHealthEvaluator.Evaluate(
            CameraHealthProblem.Busy,
            [new CameraCheckReading(150, true)],
            attemptedFrames: 1,
            new CameraCheckThresholds());

        result.Health.Should().Be(CameraHealth.Unavailable);
        result.Problem.Should().Be(CameraHealthProblem.Busy);
    }

    [Fact]
    public void Invalid_thresholds_and_negative_attempts_are_rejected()
    {
        var act = () => CameraHealthEvaluator.Evaluate(
            CameraHealthProblem.None,
            [],
            attemptedFrames: -1,
            new CameraCheckThresholds());

        act.Should().Throw<ArgumentOutOfRangeException>();

        var thresholdAct = () => CameraHealthEvaluator.Evaluate(
            CameraHealthProblem.None,
            [],
            attemptedFrames: 0,
            new CameraCheckThresholds(MinimumLitFrameRatio: 1.5));

        thresholdAct.Should().Throw<ArgumentOutOfRangeException>();

        var faceThresholdAct = () => CameraHealthEvaluator.Evaluate(
            CameraHealthProblem.None,
            [],
            attemptedFrames: 0,
            new CameraCheckThresholds(FaceLuminanceThreshold: 300));

        faceThresholdAct.Should().Throw<ArgumentOutOfRangeException>();
    }
}
