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
    private readonly object _gate = new();
    private bool _disposed;

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

        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);

            try
            {
                using var mat = ToMat(frame);
                using var gray = new Mat();
                Cv2.CvtColor(mat, gray, ColorConversionCodes.BGR2GRAY);
                luminance = Cv2.Mean(gray).Val0;

                var stopwatch = Stopwatch.StartNew();
                var face = _detector.Detect(mat);
                stopwatch.Stop();

                var faceLuminance = face.FacePresent ? ComputeFaceMedian(gray, face) : null;

                return new AttentionFrameResult(
                    face,
                    luminance,
                    stopwatch.Elapsed.TotalMilliseconds,
                    Error: null,
                    FaceLuminance: faceLuminance);
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
    }

    public void Dispose()
    {
        lock (_gate)
        {
            if (_disposed)
                return;

            _disposed = true;
            _detector.Dispose();
        }
    }

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

    // The face region is what matters for monitoring: a bright window or a dark
    // background must not override how well the face itself is exposed.
    private static double? ComputeFaceMedian(Mat gray, FaceObservation face)
    {
        var region = FaceRegion(face, gray.Width, gray.Height);
        if (region.Width <= 0 || region.Height <= 0)
            return null;

        using var roi = new Mat(gray, region).Clone();
        roi.GetArray(out byte[] pixels);
        if (pixels.Length == 0)
            return null;

        var histogram = new int[256];
        foreach (var pixel in pixels)
            histogram[pixel]++;

        var middle = pixels.Length / 2;
        var cumulative = 0;
        for (var value = 0; value < histogram.Length; value++)
        {
            cumulative += histogram[value];
            if (cumulative > middle)
                return value;
        }

        return 255;
    }

    private static Rect FaceRegion(FaceObservation face, int frameWidth, int frameHeight)
    {
        const double padding = 0.15;
        var x = Math.Max(0, (int)Math.Floor(face.X - face.Width * padding));
        var y = Math.Max(0, (int)Math.Floor(face.Y - face.Height * padding));
        var right = Math.Min(frameWidth, (int)Math.Ceiling(face.X + face.Width * (1 + padding)));
        var bottom = Math.Min(frameHeight, (int)Math.Ceiling(face.Y + face.Height * (1 + padding)));

        return new Rect(x, y, Math.Max(0, right - x), Math.Max(0, bottom - y));
    }
}
