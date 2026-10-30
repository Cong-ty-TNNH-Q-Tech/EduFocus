using EduFocus.Domain.Attention;
using EduFocus.Ports.DTOs;
using EduFocus.Ports.Inbound;
using EduFocus.Ports.Outbound.Services;

namespace EduFocus.UseCases.Attention;

/// <summary>
/// Short camera check/calibration (UC08, issue #34). Runs only while the
/// attention pipeline is stopped so it never competes with the session for the
/// device. It opens the camera, analyzes a few frames in memory, always releases
/// the camera afterwards and never stores frames or landmarks.
/// </summary>
public sealed class CameraCheckService : ICameraCheckUseCase
{
    private readonly ICameraSessionUseCase _camera;
    private readonly IAttentionFrameAnalyzer _analyzer;
    private readonly IAttentionPipelineUseCase _pipeline;
    private readonly IAppLogger _logger;
    private readonly TimeProvider _timeProvider;
    private readonly CameraCheckOptions _options;
    private readonly SemaphoreSlim _gate = new(1, 1);

    public CameraCheckService(
        ICameraSessionUseCase camera,
        IAttentionFrameAnalyzer analyzer,
        IAttentionPipelineUseCase pipeline,
        IAppLogger logger,
        TimeProvider timeProvider,
        CameraCheckOptions options)
    {
        _camera = camera ?? throw new ArgumentNullException(nameof(camera));
        _analyzer = analyzer ?? throw new ArgumentNullException(nameof(analyzer));
        _pipeline = pipeline ?? throw new ArgumentNullException(nameof(pipeline));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _timeProvider = timeProvider ?? throw new ArgumentNullException(nameof(timeProvider));

        if (options is null)
            throw new ArgumentNullException(nameof(options));
        if (options.SampleCount is < CameraCheckOptions.MinSampleCount or > CameraCheckOptions.MaxSampleCount)
            throw new ArgumentOutOfRangeException(nameof(options), "The sample count is outside the supported range.");
        if (options.SampleInterval < CameraCheckOptions.MinSampleInterval
            || options.SampleInterval > CameraCheckOptions.MaxSampleInterval)
        {
            throw new ArgumentOutOfRangeException(nameof(options), "The sample interval is outside the supported range.");
        }
        if (options.Thresholds is null)
            throw new ArgumentNullException(nameof(options), "Check thresholds are required.");

        _options = options;
    }

    public async Task<CameraHealthResult> RunAsync(CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (_pipeline.IsRunning)
                return Unavailable(CameraHealthProblem.MonitoringActive);

            var readings = new List<CameraCheckReading>(_options.SampleCount);
            var attemptedFrames = 0;
            var problem = CameraHealthProblem.None;

            try
            {
                var open = await _camera.StartAsync(cancellationToken).ConfigureAwait(false);

                if (!open.Success)
                {
                    problem = CameraProblemMapper.FromFailure(open.Failure);
                }
                else
                {
                    for (var sample = 0; sample < _options.SampleCount; sample++)
                    {
                        if (sample > 0)
                        {
                            await Task.Delay(_options.SampleInterval, _timeProvider, cancellationToken)
                                .ConfigureAwait(false);
                        }

                        attemptedFrames++;
                        var capture = await _camera.CaptureAsync(cancellationToken).ConfigureAwait(false);
                        var frame = capture.Frame;

                        if (!capture.Captured || frame is null)
                        {
                            if (readings.Count == 0)
                                problem = CameraProblemMapper.FromFailure(capture.Failure);
                            break;
                        }

                        using (frame)
                        {
                            var analysis = Analyze(frame);

                            if (analysis is { Succeeded: true })
                                readings.Add(new CameraCheckReading(analysis.Luminance, analysis.Face.FacePresent));
                        }
                    }
                }
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                _logger.Error(LogCategories.Attention, "The camera check failed unexpectedly.", ex);
                problem = CameraHealthProblem.Unknown;
            }
            finally
            {
                await StopCameraAsync().ConfigureAwait(false);
            }

            return CameraHealthEvaluator.Evaluate(problem, readings, attemptedFrames, _options.Thresholds);
        }
        finally
        {
            _gate.Release();
        }
    }

    private AttentionFrameResult? Analyze(CameraFrame frame)
    {
        try
        {
            var result = _analyzer.Analyze(frame);

            if (!result.Succeeded)
                _logger.Warning(LogCategories.Attention, $"Camera check skipped a frame: {result.Error}");

            return result;
        }
        catch (Exception ex)
        {
            _logger.Warning(
                LogCategories.Attention,
                $"Camera check could not analyze a frame: {ex.GetType().Name}.");
            return null;
        }
    }

    private async Task StopCameraAsync()
    {
        try
        {
            await _camera.StopAsync(CancellationToken.None).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.Error(LogCategories.Attention, "Could not close the camera after the check.", ex);
        }
    }

    private static CameraHealthResult Unavailable(CameraHealthProblem problem) =>
        new(CameraHealth.Unavailable, problem, AttemptedFrames: 0, AnalyzedFrames: 0, FaceFrames: 0, MeanLuminance: null);
}
