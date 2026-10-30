using System.Diagnostics;
using EduFocus.Domain.Attention;
using EduFocus.Infrastructure.Attention;
using EduFocus.Infrastructure.Attention.Camera;
using EduFocus.Ports.Outbound.Services;

namespace AttentionPipelineSpike;

public sealed record SpikeOpenResult(bool Success, double OpenMs, string CaptureApi, string? Error);

public sealed record SpikeFrameResult(
    bool Captured,
    FaceObservation? Face,
    double Luminance,
    double CaptureMs,
    double DetectMs,
    string? Error);

/// <summary>
/// Benchmark adapter that pairs the production camera port with the frame
/// analyzer, preserving the capture/detect timings the spike reports.
/// </summary>
public sealed class SpikeCameraAnalyzer : IDisposable
{
    private readonly OpenCvCameraPort _camera;
    private readonly IAttentionFrameAnalyzer _analyzer;
    private readonly string _captureApi;

    private SpikeCameraAnalyzer(
        OpenCvCameraPort camera,
        IAttentionFrameAnalyzer analyzer,
        string captureApi)
    {
        _camera = camera;
        _analyzer = analyzer;
        _captureApi = captureApi;
    }

    public string ModelInfo => _analyzer.ModelInfo;

    public long ModelLoadMs => _analyzer.ModelLoadMs;

    public bool IsOpen => _camera.IsOpen;

    public static SpikeCameraAnalyzer Create(
        string backend,
        AttentionAnalyzerOptions options,
        CliOptions cli)
    {
        var analyzer = AttentionAnalyzerFactory.Create(backend, options, cli.ModelDir);
        var camera = new OpenCvCameraPort(
            new WindowsCameraDeviceCatalog(),
            new CameraPortOptions
            {
                CaptureApi = cli.CaptureApi,
                Width = cli.Width,
                Height = cli.Height,
                FlushFrames = cli.FlushFrames
            });

        return new SpikeCameraAnalyzer(camera, analyzer, cli.CaptureApi);
    }

    public SpikeOpenResult Open()
    {
        var stopwatch = Stopwatch.StartNew();
        var result = _camera.Open(deviceId: null);
        stopwatch.Stop();

        return new SpikeOpenResult(
            result.Success,
            stopwatch.Elapsed.TotalMilliseconds,
            _captureApi,
            result.Error);
    }

    public SpikeFrameResult AnalyzeFrame()
    {
        var captureStopwatch = Stopwatch.StartNew();
        var capture = _camera.CaptureFrame();
        captureStopwatch.Stop();

        if (!capture.Captured || capture.Frame is null)
        {
            return new SpikeFrameResult(
                Captured: false,
                Face: null,
                Luminance: 0,
                CaptureMs: captureStopwatch.Elapsed.TotalMilliseconds,
                DetectMs: 0,
                Error: capture.Error ?? "frame-read-failed");
        }

        using var frame = capture.Frame;
        var analysis = _analyzer.Analyze(frame);

        return new SpikeFrameResult(
            Captured: true,
            Face: analysis.Succeeded ? analysis.Face : null,
            Luminance: analysis.Luminance,
            CaptureMs: captureStopwatch.Elapsed.TotalMilliseconds,
            DetectMs: analysis.DetectMs,
            Error: analysis.Error);
    }

    public void Dispose()
    {
        _camera.Dispose();
        _analyzer.Dispose();
    }
}
