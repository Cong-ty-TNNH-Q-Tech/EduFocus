using System.Diagnostics;
using EduFocus.Domain.Attention;
using EduFocus.Ports.DTOs;
using EduFocus.UseCases.Attention;
using EduFocus.UseCases.Tests.TestSupport;
using FluentAssertions;

namespace EduFocus.UseCases.Tests.Attention;

public sealed class AttentionPipelineServiceTests
{
    private static readonly DateTimeOffset FixedNow = new(2026, 10, 30, 9, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task Sample_once_analyzes_the_captured_frame_and_disposes_it()
    {
        var harness = new Harness();
        var frame = Frame();
        harness.Camera.CaptureResults.Enqueue(CameraCaptureResult.Success(frame));

        var sample = await harness.Service.SampleOnceAsync();

        sample.HasObservation.Should().BeTrue();
        sample.Observation.Should().Be(harness.Analyzer.DefaultResult.Face);
        sample.CameraFailure.Should().Be(CameraFailureKind.None);
        sample.Luminance.Should().Be(120);
        sample.DetectMs.Should().Be(7);
        sample.Error.Should().BeNull();
        harness.Analyzer.AnalyzeCalls.Should().Be(1);
        harness.Analyzer.FrameDisposedAtAnalyze.Should().Equal(false);
        frame.IsDisposed.Should().BeTrue();
    }

    [Fact]
    public async Task Sample_once_disposes_the_frame_before_publishing_the_sample()
    {
        var harness = new Harness();
        var frame = Frame();
        var disposedWhenPublished = true;

        harness.Camera.CaptureResults.Enqueue(CameraCaptureResult.Success(frame));
        harness.Service.SampleProduced += (_, _) => disposedWhenPublished = frame.IsDisposed;

        await harness.Service.SampleOnceAsync();

        disposedWhenPublished.Should().BeTrue();
    }

    [Fact]
    public async Task Sample_once_reports_a_camera_failure_without_running_inference()
    {
        var harness = new Harness();
        harness.Camera.CaptureResults.Enqueue(
            CameraCaptureResult.Unavailable(CameraFailureKind.Busy, "camera busy"));

        var sample = await harness.Service.SampleOnceAsync();

        sample.HasObservation.Should().BeFalse();
        sample.Observation.Should().BeNull();
        sample.CameraFailure.Should().Be(CameraFailureKind.Busy);
        sample.Error.Should().Be("camera busy");
        harness.Analyzer.AnalyzeCalls.Should().Be(0);
        harness.Logger.Warnings.Should().Contain(message => message.Contains("could not capture a frame (Busy)"));
    }

    [Fact]
    public async Task Sample_once_reports_analysis_failure_without_claiming_the_student_is_absent()
    {
        var harness = new Harness();
        var frame = Frame();
        harness.Camera.CaptureResults.Enqueue(CameraCaptureResult.Success(frame));
        harness.Analyzer.Results.Enqueue(
            new AttentionFrameResult(FaceObservation.NoFace, 90, 12, "detector-error: boom"));

        var sample = await harness.Service.SampleOnceAsync();

        sample.HasObservation.Should().BeFalse();
        sample.Observation.Should().BeNull();
        sample.CameraFailure.Should().Be(CameraFailureKind.None);
        sample.Luminance.Should().Be(90);
        sample.DetectMs.Should().Be(12);
        sample.Error.Should().Be("detector-error: boom");
        frame.IsDisposed.Should().BeTrue();
        harness.Logger.Warnings.Should().Contain(message => message.Contains("Attention analysis failed"));
    }

    [Fact]
    public async Task Sample_once_converts_analyzer_exceptions_to_a_failed_sample()
    {
        var harness = new Harness();
        var frame = Frame();
        harness.Camera.CaptureResults.Enqueue(CameraCaptureResult.Success(frame));
        harness.Analyzer.Handler = _ => throw new InvalidOperationException("boom");

        var sample = await harness.Service.SampleOnceAsync();

        sample.HasObservation.Should().BeFalse();
        sample.CameraFailure.Should().Be(CameraFailureKind.None);
        sample.Error.Should().StartWith("analysis-error: InvalidOperationException");
        frame.IsDisposed.Should().BeTrue();
    }

    [Fact]
    public async Task Sample_once_uses_the_clock_for_the_sample_timestamp()
    {
        var harness = new Harness();
        var frame = Frame();
        harness.Camera.CaptureResults.Enqueue(CameraCaptureResult.Success(frame));

        var sample = await harness.Service.SampleOnceAsync();

        sample.SampledAtUtc.Should().Be(FixedNow);
    }

    [Fact]
    public async Task Sample_once_before_start_reports_the_camera_as_not_started()
    {
        var harness = new Harness();

        var sample = await harness.Service.SampleOnceAsync();

        sample.CameraFailure.Should().Be(CameraFailureKind.NotStarted);
        sample.HasObservation.Should().BeFalse();
        harness.Analyzer.AnalyzeCalls.Should().Be(0);
    }

    [Fact]
    public async Task Loop_samples_repeatedly_until_stopped()
    {
        var harness = new Harness(TimeSpan.FromMilliseconds(20), TimeProvider.System);
        var frames = new List<CameraFrame>();
        var samples = new List<AttentionSample>();
        var threeSamples = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        harness.Camera.CaptureHandler = () =>
        {
            var frame = Frame();
            lock (frames)
                frames.Add(frame);
            return CameraCaptureResult.Success(frame);
        };

        harness.Service.SampleProduced += (_, sample) =>
        {
            lock (samples)
            {
                samples.Add(sample);
                if (samples.Count >= 3)
                    threeSamples.TrySetResult();
            }
        };

        await harness.Service.StartAsync();
        await threeSamples.Task.WaitAsync(TimeSpan.FromSeconds(5));
        await harness.Service.StopAsync();

        samples.Should().HaveCountGreaterThanOrEqualTo(3);
        samples.Should().OnlyContain(sample => sample.HasObservation);
        harness.Service.IsRunning.Should().BeFalse();
        harness.Camera.StartCalls.Should().Be(1);
        harness.Camera.StopCalls.Should().Be(1);
        harness.Analyzer.AnalyzeCalls.Should().BeGreaterThanOrEqualTo(3);

        lock (frames)
            frames.Should().OnlyContain(frame => frame.IsDisposed);
    }

    [Fact]
    public async Task Loop_keeps_sampling_after_an_analysis_error()
    {
        var harness = new Harness(TimeSpan.FromMilliseconds(20), TimeProvider.System);
        var calls = 0;
        var samples = new List<AttentionSample>();
        var twoSamples = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        harness.Camera.CaptureHandler = () => CameraCaptureResult.Success(Frame());
        harness.Analyzer.Handler = _ =>
        {
            calls++;
            if (calls == 1)
                throw new InvalidOperationException("first inference failed");

            return harness.Analyzer.DefaultResult;
        };

        harness.Service.SampleProduced += (_, sample) =>
        {
            lock (samples)
            {
                samples.Add(sample);
                if (samples.Count >= 2)
                    twoSamples.TrySetResult();
            }
        };

        await harness.Service.StartAsync();
        await twoSamples.Task.WaitAsync(TimeSpan.FromSeconds(5));
        await harness.Service.StopAsync();

        samples[0].Error.Should().StartWith("analysis-error");
        samples[1].HasObservation.Should().BeTrue();
        harness.Logger.Warnings.Should().Contain(
            message => message.Contains("Attention analysis failed: analysis-error"));
    }

    [Fact]
    public async Task Start_is_idempotent_while_running()
    {
        var harness = new Harness(TimeSpan.FromMilliseconds(20), TimeProvider.System);
        var firstSample = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        harness.Camera.CaptureHandler = () => CameraCaptureResult.Success(Frame());
        harness.Service.SampleProduced += (_, _) => firstSample.TrySetResult();

        await harness.Service.StartAsync();
        await firstSample.Task.WaitAsync(TimeSpan.FromSeconds(5));
        await harness.Service.StartAsync();

        harness.Service.IsRunning.Should().BeTrue();
        harness.Camera.StartCalls.Should().Be(1);
        harness.Camera.StopCalls.Should().Be(0);

        await harness.Service.StopAsync();
    }

    [Fact]
    public async Task Stop_before_start_does_not_touch_the_camera()
    {
        var harness = new Harness();

        await harness.Service.StopAsync();

        harness.Service.IsRunning.Should().BeFalse();
        harness.Camera.StartCalls.Should().Be(0);
        harness.Camera.StopCalls.Should().Be(0);
    }

    [Fact]
    public async Task Start_with_an_unavailable_camera_still_samples()
    {
        var harness = new Harness(TimeSpan.FromMilliseconds(20), TimeProvider.System);
        var firstSample = new TaskCompletionSource<AttentionSample>(TaskCreationOptions.RunContinuationsAsynchronously);

        harness.Camera.StartResult = CameraOpenResult.Failed(CameraFailureKind.PermissionDenied, "access denied");
        harness.Camera.CaptureHandler = () =>
            CameraCaptureResult.Unavailable(CameraFailureKind.PermissionDenied, "access denied");
        harness.Service.SampleProduced += (_, sample) => firstSample.TrySetResult(sample);

        await harness.Service.StartAsync();
        var sample = await firstSample.Task.WaitAsync(TimeSpan.FromSeconds(5));
        await harness.Service.StopAsync();

        sample.CameraFailure.Should().Be(CameraFailureKind.PermissionDenied);
        harness.Logger.Warnings.Should().Contain(
            message => message.Contains("started without a camera: PermissionDenied"));
    }

    [Fact]
    public async Task Camera_failures_are_logged_once_per_kind()
    {
        var harness = new Harness();
        harness.Camera.CaptureResults.Enqueue(
            CameraCaptureResult.Unavailable(CameraFailureKind.Busy, "busy"));
        harness.Camera.CaptureResults.Enqueue(
            CameraCaptureResult.Unavailable(CameraFailureKind.Busy, "busy"));
        harness.Camera.CaptureResults.Enqueue(CameraCaptureResult.Success(Frame()));
        harness.Camera.CaptureResults.Enqueue(
            CameraCaptureResult.Unavailable(CameraFailureKind.Busy, "busy"));

        await harness.Service.SampleOnceAsync();
        await harness.Service.SampleOnceAsync();
        await harness.Service.SampleOnceAsync();
        await harness.Service.SampleOnceAsync();

        harness.Logger.Warnings
            .Count(message => message.Contains("could not capture a frame (Busy)"))
            .Should().Be(2);
    }

    [Fact]
    public async Task Repeated_analysis_errors_are_logged_once_until_they_change()
    {
        var harness = new Harness();
        var error = new AttentionFrameResult(FaceObservation.NoFace, 80, 10, "detector-error: flaky");

        harness.Camera.CaptureHandler = () => CameraCaptureResult.Success(Frame());
        harness.Analyzer.Results.Enqueue(error);
        harness.Analyzer.Results.Enqueue(error);
        harness.Analyzer.Results.Enqueue(harness.Analyzer.DefaultResult);
        harness.Analyzer.Results.Enqueue(error);

        await harness.Service.SampleOnceAsync();
        await harness.Service.SampleOnceAsync();
        await harness.Service.SampleOnceAsync();
        await harness.Service.SampleOnceAsync();

        harness.Logger.Warnings
            .Count(message => message.Contains("Attention analysis failed: detector-error: flaky"))
            .Should().Be(2);
    }

    [Fact]
    public async Task Subscriber_exceptions_do_not_stop_the_loop()
    {
        var harness = new Harness(TimeSpan.FromMilliseconds(20), TimeProvider.System);
        harness.Camera.CaptureHandler = () => CameraCaptureResult.Success(Frame());
        harness.Service.SampleProduced += (_, _) => throw new InvalidOperationException("subscriber failed");

        await harness.Service.StartAsync();
        await WaitUntilAsync(() => harness.Analyzer.AnalyzeCalls >= 2, TimeSpan.FromSeconds(5));
        await harness.Service.StopAsync();

        harness.Logger.Errors.Should().Contain(message => message.Contains("subscriber threw an exception"));
    }

    [Fact]
    public async Task Concurrent_sample_calls_are_serialized()
    {
        var harness = new Harness();
        var firstCaptureEntered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseFirstCapture = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var captures = 0;
        var captureGate = new object();

        harness.Camera.CaptureHandler = () =>
        {
            int captured;
            lock (captureGate)
                captured = ++captures;

            if (captured == 1)
            {
                firstCaptureEntered.TrySetResult();
                releaseFirstCapture.Task.GetAwaiter().GetResult();
            }

            return CameraCaptureResult.Success(Frame());
        };

        var first = harness.Service.SampleOnceAsync();
        await firstCaptureEntered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        var second = harness.Service.SampleOnceAsync();

        await Task.Delay(100);
        harness.Camera.CaptureCalls.Should().Be(1);

        releaseFirstCapture.TrySetResult();
        await Task.WhenAll(first, second);

        harness.Camera.CaptureCalls.Should().Be(2);
    }

    [Fact]
    public async Task Cancelling_start_while_the_camera_opens_stops_the_camera()
    {
        var harness = new Harness();
        harness.Camera.StartHandler = async cancellationToken =>
        {
            await Task.Delay(TimeSpan.FromSeconds(5), cancellationToken);
            return CameraOpenResult.Opened(null);
        };

        using var cts = new CancellationTokenSource();
        var start = harness.Service.StartAsync(cts.Token);
        cts.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () => await start);

        harness.Service.IsRunning.Should().BeFalse();
        harness.Camera.StopCalls.Should().Be(1);
    }

    [Fact]
    public async Task The_pipeline_does_not_dispose_the_analyzer()
    {
        var harness = new Harness();

        await harness.Service.DisposeAsync();

        harness.Analyzer.DisposeCalls.Should().Be(0);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(5001)]
    public void Options_outside_the_supported_interval_are_rejected(int milliseconds)
    {
        var create = () => new AttentionPipelineService(
            new FakeCameraSessionUseCase(),
            new FakeAttentionFrameAnalyzer(),
            new RecordingLogger(),
            TimeProvider.System,
            new AttentionPipelineOptions(TimeSpan.FromMilliseconds(milliseconds)));

        create.Should().Throw<ArgumentOutOfRangeException>();
    }

    [Fact]
    public void Null_options_are_rejected()
    {
        var create = () => new AttentionPipelineService(
            new FakeCameraSessionUseCase(),
            new FakeAttentionFrameAnalyzer(),
            new RecordingLogger(),
            TimeProvider.System,
            options: null!);

        create.Should().Throw<ArgumentNullException>();
    }

    private static CameraFrame Frame() => new(2, 2, new byte[2 * 2 * 3]);

    private static async Task WaitUntilAsync(Func<bool> condition, TimeSpan timeout)
    {
        var stopwatch = Stopwatch.StartNew();

        while (!condition())
        {
            if (stopwatch.Elapsed > timeout)
                throw new TimeoutException("The condition was not met in time.");

            await Task.Delay(10);
        }
    }

    private sealed class Harness
    {
        public Harness(TimeSpan? interval = null, TimeProvider? timeProvider = null)
        {
            Service = new AttentionPipelineService(
                Camera,
                Analyzer,
                Logger,
                timeProvider ?? new ManualTimeProvider(FixedNow),
                new AttentionPipelineOptions(interval ?? TimeSpan.FromSeconds(1)));
        }

        public FakeCameraSessionUseCase Camera { get; } = new();

        public FakeAttentionFrameAnalyzer Analyzer { get; } = new();

        public RecordingLogger Logger { get; } = new();

        public AttentionPipelineService Service { get; }
    }
}
