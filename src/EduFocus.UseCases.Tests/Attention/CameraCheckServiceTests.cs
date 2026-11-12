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
    public async Task A_dark_face_is_reported_as_too_dark_even_when_the_room_is_bright()
    {
        var harness = new Harness();
        harness.Analyzer.DefaultResult = new AttentionFrameResult(
            FakeAttentionFrameAnalyzer.Face(), 180, 5, Error: null, FaceLuminance: 20);
        harness.Camera.CaptureResults.Enqueue(CameraCaptureResult.Success(Frame()));
        harness.Camera.CaptureResults.Enqueue(CameraCaptureResult.Success(Frame()));

        var result = await harness.Service.RunAsync();

        result.Health.Should().Be(CameraHealth.TooDark);
        result.MeanLuminance.Should().BeApproximately(180, 0.0001);
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
    public async Task The_check_synchronizes_with_real_attention_pipeline_lifecycle_gate()
    {
        var camera = new FakeCameraSessionUseCase();
        var analyzer = new FakeAttentionFrameAnalyzer();
        var logger = new RecordingLogger();
        await using var pipeline = new AttentionPipelineService(
            camera,
            analyzer,
            logger,
            TimeProvider.System,
            new AttentionPipelineOptions(TimeSpan.FromMilliseconds(500)));

        var checkService = new CameraCheckService(
            camera,
            analyzer,
            pipeline,
            logger,
            TimeProvider.System,
            new CameraCheckOptions
            {
                SampleCount = 1,
                SampleInterval = TimeSpan.FromMilliseconds(50),
                WarmUpDelay = TimeSpan.Zero
            });

        camera.CaptureResults.Enqueue(CameraCaptureResult.Success(Frame()));

        var result = await checkService.RunAsync();

        result.Health.Should().Be(CameraHealth.Ready);
        camera.StartCalls.Should().Be(1);
        camera.StopCalls.Should().Be(1);
        pipeline.IsRunning.Should().BeFalse();
        pipeline.LifecycleGate.CurrentCount.Should().Be(1);
    }

    [Fact]
    public async Task The_check_refuses_when_real_attention_pipeline_is_running()
    {
        var camera = new FakeCameraSessionUseCase();
        var analyzer = new FakeAttentionFrameAnalyzer();
        var logger = new RecordingLogger();
        await using var pipeline = new AttentionPipelineService(
            camera,
            analyzer,
            logger,
            TimeProvider.System,
            new AttentionPipelineOptions(TimeSpan.FromMilliseconds(500)));

        camera.CaptureResults.Enqueue(CameraCaptureResult.Success(Frame()));
        await pipeline.StartAsync();

        var checkService = new CameraCheckService(
            camera,
            analyzer,
            pipeline,
            logger,
            TimeProvider.System,
            new CameraCheckOptions
            {
                SampleCount = 1,
                SampleInterval = TimeSpan.FromMilliseconds(50),
                WarmUpDelay = TimeSpan.Zero
            });

        var result = await checkService.RunAsync();

        result.Health.Should().Be(CameraHealth.Unavailable);
        result.Problem.Should().Be(CameraHealthProblem.MonitoringActive);

        await pipeline.StopAsync();
    }

    [Fact]
    public async Task The_check_reports_an_in_memory_preview_for_each_captured_frame()
    {
        var harness = new Harness();
        harness.Camera.CaptureResults.Enqueue(CameraCaptureResult.Success(Frame()));
        harness.Camera.CaptureResults.Enqueue(CameraCaptureResult.Success(Frame()));
        var previews = new RecordingProgress<CameraPreviewFrame>();

        await harness.Service.RunAsync(preview: previews);

        previews.Frames.Should().HaveCount(2);
        previews.Frames.Should().OnlyContain(
            frame => frame.Width == 1 && frame.Height == 1 && frame.Bgr24.Length == 3);
    }

    [Fact]
    public async Task A_failing_preview_reporter_does_not_break_the_check()
    {
        var harness = new Harness();
        harness.Camera.CaptureResults.Enqueue(CameraCaptureResult.Success(Frame()));
        harness.Camera.CaptureResults.Enqueue(CameraCaptureResult.Success(Frame()));

        var result = await harness.Service.RunAsync(preview: new ThrowingProgress<CameraPreviewFrame>());

        result.Health.Should().Be(CameraHealth.Ready);
        harness.Logger.Warnings.Should().Contain(
            message => message.Contains("preview", StringComparison.OrdinalIgnoreCase));
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
        var warmUp = () => harness.Create(new CameraCheckOptions
        {
            WarmUpDelay = TimeSpan.FromSeconds(6)
        });

        act.Should().Throw<ArgumentOutOfRangeException>();
        interval.Should().Throw<ArgumentOutOfRangeException>();
        warmUp.Should().Throw<ArgumentOutOfRangeException>();
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
                SampleInterval = TimeSpan.FromMilliseconds(50),
                WarmUpDelay = TimeSpan.Zero
            });
        }

        public CameraCheckService Create(CameraCheckOptions options) =>
            new(Camera, Analyzer, Pipeline, Logger, TimeProvider.System, options);
    }

    private sealed class RecordingProgress<T> : IProgress<T>
    {
        public List<T> Frames { get; } = [];

        public void Report(T value) => Frames.Add(value);
    }

    private sealed class ThrowingProgress<T> : IProgress<T>
    {
        public void Report(T value) => throw new InvalidOperationException("preview handler failed");
    }
}
