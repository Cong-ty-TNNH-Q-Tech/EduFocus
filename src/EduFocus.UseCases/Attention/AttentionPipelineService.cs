using System.Diagnostics;
using EduFocus.Domain.Attention;
using EduFocus.Ports.DTOs;
using EduFocus.Ports.Inbound;
using EduFocus.Ports.Outbound.Services;

namespace EduFocus.UseCases.Attention;

/// <summary>
/// Attention sampling loop (UC08, issue #31): captures through the camera
/// supervisor, runs inference on the managed frame, disposes the frame before
/// publishing the sample and never persists image data. Classification,
/// aggregation and session coverage belong to issues #32/#33.
/// </summary>
public sealed class AttentionPipelineService : IAttentionPipelineUseCase, IAsyncDisposable
{
    private const string DefaultCaptureError = "The camera returned no frame.";

    private readonly ICameraSessionUseCase _camera;
    private readonly IAttentionFrameAnalyzer _analyzer;
    private readonly IAppLogger _logger;
    private readonly TimeProvider _timeProvider;
    private readonly AttentionPipelineOptions _options;
    private readonly SemaphoreSlim _lifecycleGate = new(1, 1);
    private readonly SemaphoreSlim _sampleGate = new(1, 1);

    private CancellationTokenSource? _loopCts;
    private Task? _loop;
    private volatile bool _running;
    private bool _cameraOwned;
    private CameraFailureKind _lastCameraFailure = CameraFailureKind.None;
    private string? _lastAnalysisError;

    public AttentionPipelineService(
        ICameraSessionUseCase camera,
        IAttentionFrameAnalyzer analyzer,
        IAppLogger logger,
        TimeProvider timeProvider,
        AttentionPipelineOptions options)
    {
        _camera = camera ?? throw new ArgumentNullException(nameof(camera));
        _analyzer = analyzer ?? throw new ArgumentNullException(nameof(analyzer));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _timeProvider = timeProvider ?? throw new ArgumentNullException(nameof(timeProvider));

        if (options is null)
            throw new ArgumentNullException(nameof(options));
        if (options.SampleInterval <= TimeSpan.Zero || options.SampleInterval > TimeSpan.FromSeconds(5))
            throw new ArgumentOutOfRangeException(nameof(options), "Sample interval must be between 0 and 5 seconds.");

        _options = options;
    }

    public bool IsRunning => _running;

    public event EventHandler<AttentionSample>? SampleProduced;

    public async Task StartAsync(CancellationToken cancellationToken = default)
    {
        await _lifecycleGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (_running)
                return;

            await StopCoreAsync().ConfigureAwait(false);

            _cameraOwned = true;
            CameraOpenResult open;

            try
            {
                open = await _camera.StartAsync(cancellationToken).ConfigureAwait(false);
            }
            catch
            {
                await StopCoreAsync().ConfigureAwait(false);
                throw;
            }

            if (!open.Success)
            {
                _logger.Warning(
                    LogCategories.Attention,
                    $"Attention sampling started without a camera: {open.Failure}.");
            }

            _lastCameraFailure = CameraFailureKind.None;
            _lastAnalysisError = null;
            _running = true;

            var cts = new CancellationTokenSource();
            _loopCts = cts;
            _loop = Task.Run(() => LoopAsync(cts.Token), CancellationToken.None);

            _logger.Info(
                LogCategories.Attention,
                $"Attention sampling started: one sample every {_options.SampleInterval.TotalMilliseconds:F0} ms.");
        }
        finally
        {
            _lifecycleGate.Release();
        }
    }

    public async Task StopAsync(CancellationToken cancellationToken = default)
    {
        await _lifecycleGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await StopCoreAsync().ConfigureAwait(false);
        }
        finally
        {
            _lifecycleGate.Release();
        }
    }

    public async Task<AttentionSample> SampleOnceAsync(CancellationToken cancellationToken = default)
    {
        AttentionSample sample;

        await _sampleGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            sample = await SampleCoreAsync(cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _sampleGate.Release();
        }

        Publish(sample);
        return sample;
    }

    public async ValueTask DisposeAsync()
    {
        await StopAsync(CancellationToken.None).ConfigureAwait(false);
        _lifecycleGate.Dispose();
        _sampleGate.Dispose();
    }

    private async Task StopCoreAsync()
    {
        var cts = _loopCts;
        var loop = _loop;
        _loopCts = null;
        _loop = null;

        cts?.Cancel();

        if (loop is not null)
        {
            try
            {
                await loop.ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
            }
            catch (Exception ex)
            {
                _logger.Error(LogCategories.Attention, "The attention sampling loop stopped unexpectedly.", ex);
            }
        }

        cts?.Dispose();
        _running = false;

        if (_cameraOwned)
        {
            _cameraOwned = false;
            try
            {
                await _camera.StopAsync(CancellationToken.None).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                _logger.Error(LogCategories.Attention, "Could not stop the camera session.", ex);
            }
        }

        if (loop is not null)
            _logger.Info(LogCategories.Attention, "Attention sampling stopped.");
    }

    private async Task LoopAsync(CancellationToken cancellationToken)
    {
        try
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                try
                {
                    await SampleOnceAsync(cancellationToken).ConfigureAwait(false);
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                    break;
                }
                catch (Exception ex)
                {
                    _logger.Error(LogCategories.Attention, "Attention sampling tick failed.", ex);
                }

                await Task.Delay(_options.SampleInterval, _timeProvider, cancellationToken)
                    .ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
        finally
        {
            _running = false;
        }
    }

    private async Task<AttentionSample> SampleCoreAsync(CancellationToken cancellationToken)
    {
        var sampledAt = _timeProvider.GetUtcNow();
        var captureStopwatch = Stopwatch.StartNew();
        CameraCaptureResult capture;

        try
        {
            capture = await _camera.CaptureAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            captureStopwatch.Stop();
            _logger.Warning(
                LogCategories.Attention,
                $"Camera capture threw an unexpected error: {ex.GetType().Name}.");

            return new AttentionSample(
                sampledAt,
                CameraFailureKind.Unknown,
                Observation: null,
                Luminance: 0,
                CaptureMs: captureStopwatch.Elapsed.TotalMilliseconds,
                DetectMs: 0,
                Error: ex.Message);
        }

        captureStopwatch.Stop();

        if (!capture.Captured || capture.Frame is null)
        {
            var failure = capture.Failure == CameraFailureKind.None
                ? CameraFailureKind.Unknown
                : capture.Failure;

            return BuildCameraFailure(
                sampledAt,
                failure,
                capture.Error,
                captureStopwatch.Elapsed.TotalMilliseconds);
        }

        using (var frame = capture.Frame)
        {
            return AnalyzeFrame(frame, sampledAt, captureStopwatch.Elapsed.TotalMilliseconds);
        }
    }

    private AttentionSample AnalyzeFrame(
        CameraFrame frame,
        DateTimeOffset sampledAt,
        double captureMs)
    {
        AttentionFrameResult result;

        try
        {
            result = _analyzer.Analyze(frame);
        }
        catch (Exception ex)
        {
            result = new AttentionFrameResult(
                FaceObservation.NoFace,
                Luminance: 0,
                DetectMs: 0,
                Error: $"analysis-error: {ex.GetType().Name}: {ex.Message}");
        }

        if (_lastCameraFailure != CameraFailureKind.None)
        {
            _lastCameraFailure = CameraFailureKind.None;
            _logger.Info(LogCategories.Attention, "Attention sampling resumed.");
        }

        if (!result.Succeeded)
        {
            LogAnalysisFailureOnce(result.Error!);

            return new AttentionSample(
                sampledAt,
                CameraFailureKind.None,
                Observation: null,
                Luminance: result.Luminance,
                CaptureMs: captureMs,
                DetectMs: result.DetectMs,
                Error: result.Error);
        }

        _lastAnalysisError = null;

        return new AttentionSample(
            sampledAt,
            CameraFailureKind.None,
            Observation: result.Face,
            Luminance: result.Luminance,
            CaptureMs: captureMs,
            DetectMs: result.DetectMs,
            Error: null);
    }

    private AttentionSample BuildCameraFailure(
        DateTimeOffset sampledAt,
        CameraFailureKind failure,
        string? error,
        double captureMs)
    {
        if (_lastCameraFailure != failure)
        {
            _lastCameraFailure = failure;
            _logger.Warning(
                LogCategories.Attention,
                $"Attention sampling could not capture a frame ({failure}).");
        }

        return new AttentionSample(
            sampledAt,
            failure,
            Observation: null,
            Luminance: 0,
            CaptureMs: captureMs,
            DetectMs: 0,
            Error: error ?? DefaultCaptureError);
    }

    private void LogAnalysisFailureOnce(string error)
    {
        if (string.Equals(_lastAnalysisError, error, StringComparison.Ordinal))
            return;

        _lastAnalysisError = error;
        _logger.Warning(LogCategories.Attention, $"Attention analysis failed: {error}");
    }

    private void Publish(AttentionSample sample)
    {
        try
        {
            SampleProduced?.Invoke(this, sample);
        }
        catch (Exception ex)
        {
            _logger.Error(LogCategories.Attention, "An attention sample subscriber threw an exception.", ex);
        }
    }
}
