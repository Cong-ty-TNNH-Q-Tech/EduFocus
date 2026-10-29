using System.Diagnostics;
using EduFocus.Domain.Attention;
using EduFocus.Infrastructure.Attention.Detection;
using EduFocus.Ports.DTOs;
using EduFocus.Ports.Outbound.Services;
using OpenCvSharp;

namespace EduFocus.Infrastructure.Attention;

/// <summary>
/// OpenCV/ONNX frame analyzer. Consumes a managed BGR24 frame captured by the
/// camera supervisor (issue #30), never opens a camera and never writes frame
/// bytes or landmarks to disk or network (issue #31).
/// </summary>
public sealed class OpenCvAttentionFrameAnalyzer : IAttentionFrameAnalyzer
{
    private readonly IFaceDetector _detector;

    public OpenCvAttentionFrameAnalyzer(IFaceDetector detector)
    {
        _detector = detector ?? throw new ArgumentNullException(nameof(detector));
    }

    public string BackendName => _detector.Name;

    public string ModelInfo => _detector.Model;

    public long ModelLoadMs => _detector.LoadMs;

    public AttentionFrameResult Analyze(CameraFrame frame)
    {
        ArgumentNullException.ThrowIfNull(frame);

        var luminance = 0.0;

        try
        {
            using var mat = ToMat(frame);
            luminance = ComputeLuminance(mat);

            var stopwatch = Stopwatch.StartNew();
            var face = _detector.Detect(mat);
            stopwatch.Stop();

            return new AttentionFrameResult(
                face,
                luminance,
                stopwatch.Elapsed.TotalMilliseconds,
                Error: null);
        }
        catch (Exception ex)
        {
            return new AttentionFrameResult(
                FaceObservation.NoFace,
                luminance,
                DetectMs: 0,
                Error: $"detector-error: {ex.GetType().Name}: {ex.Message}");
        }
    }

    public void Dispose() => _detector.Dispose();

    private static Mat ToMat(CameraFrame frame)
    {
        // Mat.FromPixelData pins the array for the Mat lifetime; the frame is
        // small (640×480 BGR24) and lives only for this analysis.
        var pixels = frame.Pixels.ToArray();
        return Mat.FromPixelData(
            frame.Height,
            frame.Width,
            MatType.CV_8UC3,
            pixels,
            (long)frame.Width * 3);
    }

    private static double ComputeLuminance(Mat frame)
    {
        using var gray = new Mat();
        Cv2.CvtColor(frame, gray, ColorConversionCodes.BGR2GRAY);
        return Cv2.Mean(gray).Val0;
    }
}
