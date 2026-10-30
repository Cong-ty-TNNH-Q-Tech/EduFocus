using EduFocus.Domain.Attention;
using EduFocus.Ports.DTOs;
using EduFocus.UseCases.Attention;
using EduFocus.UseCases.Tests.TestSupport;
using FluentAssertions;

namespace EduFocus.UseCases.Tests.Attention;

public sealed class CameraCheckServiceTests
{
    [Fact]
    public async Task Bright_frames_with_a_face_are_ready_and_the_camera_is_released()
    {
        var harness = new Harness();
        var first = Frame();
        var second = Frame();
        harness.Camera.CaptureResults.Enqueue(CameraCaptureResult.Success(first));
        harness.Camera.CaptureResults.Enqueue(CameraCaptureResult.Success(second));

        var result = await harness.Service.RunAsync();

        result.Health.Should().Be(CameraHealth.Ready);
        result.Problem.Should().Be(CameraHealthProblem.None);
        result.AttemptedFrames.Should().Be(2);
        result.AnalyzedFrames.Should().Be(2);
        result.FaceFrames.Should().Be(2);
        result.MeanLuminance.Should().BeApproximately(120, 0.0001);
        harness.Analyzer.AnalyzeCalls.Should().Be(2);
        harness.Camera.StartCalls.Should().Be(1);
        harness.Camera.StopCalls.Should().Be(1);
        harness.Camera.State.Should().Be(CameraSessionState.Off);
        first.IsDisposed.Should().BeTrue();
        second.IsDisposed.Should().BeTrue();
    }

    [Fact]
    public async Task Dark_frames_are_reported_as_too_dark()
    {
        var harness = new Harness();
        harness.Analyzer.DefaultResult = new AttentionFrameResult(FakeAttentionFrameAnalyzer.Face(), 8, 3, null);
        harness.Camera.CaptureResults.Enqueue(CameraCaptureResult.Success(Frame()));
        harness.Camera.CaptureResults.Enqueue(CameraCaptureResult.Success(Frame()));

        var result = await harness.Service.RunAsync();

        result.Health.Should().Be(CameraHealth.TooDark);
        result.MeanLuminance.Should().BeApproximately(8, 0.0001);
    }

    [Fact]
    public async Task A_busy_camera_is_reported_without_analyzing_frames()
    {
        var harness = new Harness();
        harness.Camera.StartResult = CameraOpenResult.Failed(CameraFailureKind.Busy, "camera in use");

        var result = await harness.Service.RunAsync();

        result.Health.Should().Be(CameraHealth.Unavailable);
        result.Problem.Should().Be(CameraHealthProblem.Busy);
        result.AttemptedFrames.Should().Be(0);
        harness.Analyzer.AnalyzeCalls.Should().Be(0);
        harness.Camera.StopCalls.Should().Be(1);
    }

    [Fact]
    public async Task A_camera_blocked_by_windows_is_reported_as_a_permission_problem()
    {
        var harness = new Harness();
        harness.Camera.StartResult = CameraOpenResult.Failed(CameraFailureKind.PermissionDenied, "access denied");

        var result = await harness.Service.RunAsync();

        result.Problem.Should().Be(CameraHealthProblem.PermissionDenied);
    }

    [Fact]
    public async Task A_missing_camera_is_reported_as_no_camera()
    {
        var harness = new Harness();
        harness.Camera.StartResult = CameraOpenResult.Failed(CameraFailureKind.NotFound, "no device");

        var result = await harness.Service.RunAsync();

        result.Problem.Should().Be(CameraHealthProblem.NoCamera);
    }

    [Fact]
    public async Task No_analyzable_frames_are_reported_as_no_image()
    {
        var harness = new Harness();
        harness.Analyzer.Results.Enqueue(new AttentionFrameResult(FaceObservation.NoFace, 0, 1, "detector-error"));
        harness.Analyzer.Results.Enqueue(new AttentionFrameResult(FaceObservation.NoFace, 0, 1, "detector-error"));
        harness.Camera.CaptureResults.Enqueue(CameraCaptureResult.Success(Frame()));
        harness.Camera.CaptureResults.Enqueue(CameraCaptureResult.Success(Frame()));

        var result = await harness.Service.RunAsync();

        result.Health.Should().Be(CameraHealth.NoImage);
        result.AttemptedFrames.Should().Be(2);
        result.AnalyzedFrames.Should().Be(0);
    }

    [Fact]
    public async Task An_analyzer_exception_still_releases_the_camera()
    {
        var harness = new Harness();
        var first = Frame();
        var second = Frame();
        harness.Analyzer.Handler = _ => throw new InvalidOperationException("model failed");
        harness.Camera.CaptureResults.Enqueue(CameraCaptureResult.Success(first));
        harness.Camera.CaptureResults.Enqueue(CameraCaptureResult.Success(second));

        var result = await harness.Service.RunAsync();

        result.Health.Should().Be(CameraHealth.NoImage);
        result.AttemptedFrames.Should().Be(2);
        first.IsDisposed.Should().BeTrue();
        second.IsDisposed.Should().BeTrue();
        harness.Camera.StopCalls.Should().Be(1);
        harness.Logger.Warnings.Should().Contain(message => message.Contains("could not analyze"));
    }

    [Fact]
    public async Task The_check_refuses_to_run_while_the_pipeline_is_monitoring()
    {
        var harness = new Harness();
        await harness.Pipeline.StartAsync();

        var result = await harness.Service.RunAsync();

        result.Health.Should().Be(CameraHealth.Unavailable);
        result.Problem.Should().Be(CameraHealthProblem.MonitoringActive);
        harness.Camera.StartCalls.Should().Be(0);
        harness.Camera.StopCalls.Should().Be(0);
    }

    [Fact]
    public void Constructor_rejects_invalid_options()
    {
        var harness = new Harness();

        var act = () => harness.Create(new CameraCheckOptions { SampleCount = 0 });
        var interval = () => harness.Create(new CameraCheckOptions
        {
            SampleInterval = TimeSpan.FromMilliseconds(1)
        });

        act.Should().Throw<ArgumentOutOfRangeException>();
        interval.Should().Throw<ArgumentOutOfRangeException>();
    }

    private static CameraFrame Frame() => new(1, 1, [1, 2, 3]);

    private sealed class Harness
    {
        public FakeCameraSessionUseCase Camera { get; } = new();

        public FakeAttentionFrameAnalyzer Analyzer { get; } = new();

        public FakeAttentionPipelineUseCase Pipeline { get; } = new();

        public RecordingLogger Logger { get; } = new();

        public CameraCheckService Service { get; }

        public Harness()
        {
            Service = Create(new CameraCheckOptions
            {
                SampleCount = 2,
                SampleInterval = TimeSpan.FromMilliseconds(50)
            });
        }

        public CameraCheckService Create(CameraCheckOptions options) =>
            new(Camera, Analyzer, Pipeline, Logger, TimeProvider.System, options);
    }
}
