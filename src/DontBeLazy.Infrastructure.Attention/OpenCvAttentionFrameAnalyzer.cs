using DontBeLazy.Infrastructure.Attention.Camera;
using DontBeLazy.Infrastructure.Attention.Detection;
using DontBeLazy.Ports.DTOs;
using DontBeLazy.Ports.Outbound.Services;
using OpenCvSharp;

namespace DontBeLazy.Infrastructure.Attention;

public sealed class OpenCvAttentionFrameAnalyzer : IAttentionFrameAnalyzer
{
    private readonly OpenCvCameraSource _camera;
    private readonly IFaceDetector _detector;
    private readonly AttentionAnalyzerOptions _options;

    public OpenCvAttentionFrameAnalyzer(IFaceDetector detector, AttentionAnalyzerOptions options)
    {
        _detector = detector;
        _options = options;
        _camera = new OpenCvCameraSource(
            options.CameraIndex,
            options.CaptureApi,
            options.Width,
            options.Height);
    }

    public string BackendName => _detector.Name;
    public string ModelInfo => _detector.Model;
    public long ModelLoadMs => _detector.LoadMs;
    public bool IsOpen => _camera.IsOpen;

    public AttentionOpenResult Open()
    {
        var result = _camera.Open();
        return new AttentionOpenResult(result.Success, result.ElapsedMs, _options.CaptureApi, result.Error);
    }

    public AttentionFrameResult AnalyzeFrame()
    {
        using var frame = new Mat();
        if (!_camera.IsOpen)
            return new AttentionFrameResult(false, false, null, 0, 0, 0, "camera-not-open");

        if (!_camera.TryCapture(frame, _options.FlushFrames, out var captureMs, out var captureError))
        {
            _camera.Dispose();
            return new AttentionFrameResult(false, false, null, 0, captureMs, 0, captureError ?? "frame-read-failed");
        }

        var luminance = ComputeLuminance(frame);
        try
        {
            var start = System.Diagnostics.Stopwatch.StartNew();
            var face = _detector.Detect(frame);
            start.Stop();

            return new AttentionFrameResult(
                CameraAvailable: true,
                Captured: true,
                Face: face,
                Luminance: luminance,
                CaptureMs: captureMs,
                DetectMs: start.Elapsed.TotalMilliseconds,
                Error: null);
        }
        catch (Exception ex)
        {
            return new AttentionFrameResult(
                CameraAvailable: true,
                Captured: true,
                Face: null,
                Luminance: luminance,
                CaptureMs: captureMs,
                DetectMs: 0,
                Error: $"detector-error: {ex.GetType().Name}: {ex.Message}");
        }
    }

    private static double ComputeLuminance(Mat frame)
    {
        using var gray = new Mat();
        Cv2.CvtColor(frame, gray, ColorConversionCodes.BGR2GRAY);
        return Cv2.Mean(gray).Val0;
    }

    public void Dispose()
    {
        _camera.Dispose();
        _detector.Dispose();
    }
}
